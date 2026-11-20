using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace MetroRail.Ticketing;

/// <summary>
/// Ticket cryptography. Format spec: contracts/ticket-qr.md (the customer app implements the same).
///
/// 1. Certificate (server, after payment):  cert = b64u(payload) + "." + b64u(Ed25519(payload))
///    payload = {v, tid, uid, prd, from, to, line, trips, vf, vt, dpk}; dpk = the phone's P-256 public key (SPKI).
/// 2. QR (phone, every 30 s): inner = {c: cert, s: unix/30, n: nonce, k: trip hint}, d = ECDSA-P256-SHA256(inner).
/// 3. Sealed so only MetroRail scanners can read it: X25519 + HKDF-SHA256 + AES-256-GCM, "MR1:" prefix.
/// </summary>
public sealed class TicketException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}

public static class TicketCrypto
{
    public const int SlotSeconds = 30;
    public const string Prefix = "MR1:";
    private static readonly byte[] Info = "metrorail-ticket-qr-v1"u8.ToArray();
    private static readonly byte[] Aad = Encoding.ASCII.GetBytes(Prefix);

    public static string B64u(ReadOnlySpan<byte> b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] UnB64u(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(t + new string('=', (4 - t.Length % 4) % 4));
    }

    /// <summary>Same bytes as Python json.dumps(sort_keys=True, separators=(",",":")) for ASCII payloads.</summary>
    public static byte[] Canonical(JsonNode? node)
    {
        var sb = new StringBuilder();
        Write(node, sb);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void Write(JsonNode? n, StringBuilder sb)
    {
        switch (n)
        {
            case null: sb.Append("null"); break;
            case JsonObject o:
                sb.Append('{');
                var first = true;
                foreach (var kv in o.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(JsonSerializer.Serialize(kv.Key, Common.Json.Options)).Append(':');
                    Write(kv.Value, sb);
                }
                sb.Append('}');
                break;
            case JsonArray a:
                sb.Append('[');
                for (var i = 0; i < a.Count; i++) { if (i > 0) sb.Append(','); Write(a[i], sb); }
                sb.Append(']');
                break;
            default: sb.Append(n.ToJsonString(Common.Json.Options)); break;
        }
    }

    // ---- keys ----
    public sealed class Keys
    {
        public required Ed25519PrivateKeyParameters Issuer { get; init; }
        public required X25519PrivateKeyParameters Scanner { get; init; } // scanners hold this (Android Keystore)

        public static Keys LoadOrGenerate(string? issuerPem, string? scannerPem)
        {
            var rng = new SecureRandom();
            return new Keys
            {
                Issuer = issuerPem is { Length: > 0 } ? (Ed25519PrivateKeyParameters)LoadPem(issuerPem) : new Ed25519PrivateKeyParameters(rng),
                Scanner = scannerPem is { Length: > 0 } ? (X25519PrivateKeyParameters)LoadPem(scannerPem) : new X25519PrivateKeyParameters(rng),
            };
        }

        private static object LoadPem(string pem)
        {
            var body = string.Concat(pem.Split('\n').Where(l => !l.StartsWith("-----")).Select(l => l.Trim()));
            return PrivateKeyFactory.CreateKey(Convert.FromBase64String(body));
        }

        public object Public() => new
        {
            issuer_ed25519 = B64u(Issuer.GeneratePublicKey().GetEncoded()),
            scanner_x25519 = B64u(Scanner.GeneratePublicKey().GetEncoded()),
        };
    }

    // ---- 1. certificate ----
    public static string IssueCert(Ed25519PrivateKeyParameters issuer, JsonObject payload)
    {
        var body = Canonical(payload);
        var signer = new Ed25519Signer();
        signer.Init(true, issuer);
        signer.BlockUpdate(body, 0, body.Length);
        return B64u(body) + "." + B64u(signer.GenerateSignature());
    }

    public static JsonObject OpenCert(Ed25519PublicKeyParameters issuerPub, string cert)
    {
        try
        {
            var parts = cert.Split('.');
            var body = UnB64u(parts[0]);
            var v = new Ed25519Signer();
            v.Init(false, issuerPub);
            v.BlockUpdate(body, 0, body.Length);
            if (parts.Length != 2 || !v.VerifySignature(UnB64u(parts[1]))) throw new TicketException("invalid_ticket");
            return JsonNode.Parse(body)!.AsObject();
        }
        catch (Exception e) when (e is not TicketException) { throw new TicketException("invalid_ticket"); }
    }

    public static JsonObject CertPayload(string cert) => JsonNode.Parse(UnB64u(cert.Split('.')[0]))!.AsObject();

    // ---- 3. sealing ----
    private static byte[] Kdf(byte[] shared, byte[] epk) => HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, epk, Info);

    public static byte[] Seal(byte[] scannerPub, byte[] plaintext)
    {
        var eph = new X25519PrivateKeyParameters(new SecureRandom());
        var epk = eph.GeneratePublicKey().GetEncoded();
        var shared = new byte[32];
        eph.GenerateSecret(new X25519PublicKeyParameters(scannerPub), shared, 0);
        var iv = RandomNumberGenerator.GetBytes(12);
        var ct = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(Kdf(shared, epk), 16);
        aes.Encrypt(iv, plaintext, ct, tag, Aad);
        return [.. epk, .. iv, .. ct, .. tag];
    }

    public static byte[] Unseal(X25519PrivateKeyParameters scanner, byte[] sealed_)
    {
        if (sealed_.Length < 32 + 12 + 16) throw new TicketException("invalid_code");
        try
        {
            var epk = sealed_[..32];
            var iv = sealed_[32..44];
            var ct = sealed_[44..^16];
            var tag = sealed_[^16..];
            var shared = new byte[32];
            scanner.GenerateSecret(new X25519PublicKeyParameters(epk), shared, 0);
            var plain = new byte[ct.Length];
            using var aes = new AesGcm(Kdf(shared, epk), 16);
            aes.Decrypt(iv, ct, tag, plain, Aad);
            return plain;
        }
        catch (Exception) { throw new TicketException("invalid_code"); }
    }

    // ---- 2. QR: reference implementation of what the phone does (used by tests and tools) ----
    public static (ECDsa Key, string SpkiB64u) DeviceKeyPair()
    {
        var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (k, B64u(k.ExportSubjectPublicKeyInfo()));
    }

    public static string BuildQr(string cert, ECDsa deviceKey, string scannerPubB64u, int tripHint = 0, DateTimeOffset? now = null)
    {
        var inner = new JsonObject
        {
            ["c"] = cert, ["s"] = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / SlotSeconds,
            ["n"] = B64u(RandomNumberGenerator.GetBytes(8)), ["k"] = tripHint,
        };
        var sig = deviceKey.SignData(Canonical(inner), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        inner["d"] = B64u(sig);
        return Prefix + B64u(Seal(UnB64u(scannerPubB64u), Canonical(inner)));
    }

    public sealed record ScannedQr(JsonObject Cert, long Slot, string Nonce);

    /// <summary>Everything a scanner can check offline. Throws TicketException(reason).</summary>
    public static ScannedQr ReadQr(Keys keys, string qr, DateTimeOffset? now = null, int maxSlotSkew = 1)
    {
        if (!qr.StartsWith(Prefix)) throw new TicketException("not_a_metrorail_code");
        JsonObject inner;
        try { inner = JsonNode.Parse(Unseal(keys.Scanner, UnB64u(qr[Prefix.Length..])))!.AsObject(); }
        catch (Exception e) when (e is not TicketException) { throw new TicketException("invalid_code"); }

        var cert = OpenCert(keys.Issuer.GeneratePublicKey(), inner["c"]!.GetValue<string>());
        try
        {
            using var dpk = ECDsa.Create();
            dpk.ImportSubjectPublicKeyInfo(UnB64u(cert["dpk"]!.GetValue<string>()), out _);
            var sig = UnB64u(inner["d"]!.GetValue<string>());
            inner.Remove("d");
            var format = sig.Length == 64 ? DSASignatureFormat.IeeeP1363FixedFieldConcatenation : DSASignatureFormat.Rfc3279DerSequence;
            if (!dpk.VerifyData(Canonical(inner), sig, HashAlgorithmName.SHA256, format)) throw new TicketException("not_this_phone");
        }
        catch (Exception e) when (e is not TicketException) { throw new TicketException("not_this_phone"); }

        var slot = inner["s"]!.GetValue<long>();
        var slotNow = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / SlotSeconds;
        if (Math.Abs(slotNow - slot) > maxSlotSkew) throw new TicketException("expired_code");
        return new ScannedQr(cert, slot, inner["n"]!.GetValue<string>());
    }
}
