using System.Security.Cryptography;
using System.Text;
using MetroRail.Common;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MetroRail.Identity;

/// <summary>
/// ES256 (ECDSA P-256) access tokens + a JWKS so every other service verifies them offline.
/// IDENTITY_SIGNING_KEY (PEM, P-256 private key) is required in production; local/test generate one per start.
/// Token times always use the real clock, never DEMO_TIME.
/// </summary>
public sealed class Signer
{
    public static readonly TimeSpan AccessTtl = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RefreshTtl = TimeSpan.FromDays(30);

    private readonly ECDsaSecurityKey _key;
    private readonly string _issuer, _audience;
    private readonly JsonWebTokenHandler _handler = new();

    public Signer(string? privatePem, string issuer, string audience)
    {
        var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (!string.IsNullOrWhiteSpace(privatePem)) ec.ImportFromPem(privatePem);
        var pub = ec.ExportParameters(false);
        var kid = Convert.ToHexString(SHA256.HashData([.. pub.Q.X!, .. pub.Q.Y!]))[..16].ToLowerInvariant();
        _key = new ECDsaSecurityKey(ec) { KeyId = kid };
        (_issuer, _audience) = (issuer, audience);
        PublicPem = ec.ExportSubjectPublicKeyInfoPem();
        Jwk = new Dictionary<string, string>
        {
            ["kty"] = "EC", ["crv"] = "P-256", ["use"] = "sig", ["alg"] = "ES256", ["kid"] = kid,
            ["x"] = Base64UrlEncoder.Encode(pub.Q.X), ["y"] = Base64UrlEncoder.Encode(pub.Q.Y),
        };
    }

    public string PublicPem { get; }
    public Dictionary<string, string> Jwk { get; }

    public string AccessToken(string userId, IEnumerable<string> roles)
    {
        var now = Clock.RealUtcNow.UtcDateTime;
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _issuer, Audience = _audience, IssuedAt = now, NotBefore = now, Expires = now + AccessTtl,
            Claims = new Dictionary<string, object> { ["sub"] = userId, ["roles"] = roles.ToArray() },
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.EcdsaSha256),
        });
    }

    public static (string Token, string Hash) NewRefreshToken()
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

/// <summary>Delivers login codes. Local/test: logged (and returned as dev_code). Production: plug in an SMS
/// gateway (e.g. Clickatell, BulkSMS) and an email provider here.</summary>
public interface ICodeSender
{
    Task SendAsync(string channel, string target, string code);
}

public sealed class LogCodeSender(ILogger<LogCodeSender> log) : ICodeSender
{
    public Task SendAsync(string channel, string target, string code)
    {
        log.LogInformation("login_code_dev_only channel={Channel} target=…{Last4} code={Code}", channel, target[^4..], code);
        return Task.CompletedTask;
    }
}
