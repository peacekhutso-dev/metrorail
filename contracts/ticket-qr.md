# Ticket QR format (v1)

Reference implementations — keep these three in sync:

| Where | File |
|---|---|
| Server (issue + validate) | `services/Ticketing/TicketCrypto.cs` (C#) |
| Customer app (build QR) | `apps/customer/src/ticket/qr.ts` |
| Scanner app (validate offline) | `apps/scanner/` *(to build — port `ReadQr`)* |

Interop check: `dotnet run --project tools/MetroRail.Tools -- qr-interop` (TypeScript QR → C# validator), also run in CI.

## 1. Ticket certificate — issued by the server after payment

```
cert = b64u(payload_json) + "." + b64u(Ed25519_sign(issuer_key, payload_json))
payload = {"v":1, "tid":"MR-48213", "uid":"USR-…", "prd":"ten_trip", "from":"ST-RONDEBOSCH",
           "to":"ST-CLAREMONT", "line":"LN-SOUTHERN", "trips":10, "vf":<unix>, "vt":<unix>,
           "dpk":"<phone P-256 public key, SPKI DER, b64u>"}
```
JSON is canonical: keys sorted (ordinal), no spaces, ASCII.
`trips: null` = unlimited (weekly/monthly). A top-up re-issues the certificate; older ones are refused (`replaced_ticket`).

## 2. QR content — built by the phone every 30 s

```
inner = {"c": cert, "s": floor(unix_seconds / 30), "n": b64u(8 random bytes), "k": trip_hint}
d     = ECDSA_P256_SHA256(phone_private_key, canonical(inner))   # raw r||s (64 B) or DER
plain = canonical({...inner, "d": b64u(d)})
```
The phone's private key never leaves the device (WebCrypto non-extractable key now; Android Keystore / iOS Secure Enclave via a Capacitor plugin later).

## 3. Sealing — only MetroRail scanners can read it

```
eph        = new X25519 key pair
key        = HKDF-SHA256(ikm = X25519(eph, scanner_pub), salt = eph_pub, info = "metrorail-ticket-qr-v1", 32 B)
ciphertext = AES-256-GCM(key, iv = 12 random bytes, aad = "MR1:", plain)
QR         = "MR1:" + b64u(eph_pub(32) || iv(12) || ciphertext||tag)
```
A normal camera app shows `MR1:…` gibberish — no names, routes or ticket numbers.

## Validation order (scanner, offline-capable)

| # | Check | Reject reason |
|---|---|---|
| 1 | Starts with `MR1:` | `not_a_metrorail_code` |
| 2 | Unseals with the scanner key | `invalid_code` |
| 3 | Certificate signature (issuer Ed25519) | `invalid_ticket` |
| 4 | Phone signature with `dpk` | `not_this_phone` |
| 5 | `|now_slot − s| ≤ 1` (≈30–60 s) | `expired_code` |
| 6 | Nonce `n` not seen before for this ticket | `already_scanned` |
| 7 | Inside `vf`–`vt` | `outside_validity` |
| 8 | Train's line matches | `wrong_route` |
| 9 | Open trip window? same trip : consume a trip (if any left) | `no_trips_left` |

**Trip window** (one trip = whole journey incl. changes and repeat scans):
`scheduled_minutes × 1.5 + 30 × changes`, clamped to 90–240 min.

Scanners sync seen nonces and trip starts every 60 s when online; the server is the source of truth.
