# ADR 0002 — Identity service issues its own ES256 tokens for the MVP (Keycloak later)

**Status:** accepted for MVP (updated for .NET)

**Context.** The architecture spec names Keycloak (OIDC, passwordless). For the MVP we need phone/
email code login, short-lived tokens every service can verify offline, and POPIA export/delete.

**Decision.** `services/Identity` issues ES256 (ECDSA P-256) JWTs (15 min) + rotating refresh tokens
(30 days) and publishes a JWKS. All services verify via `JWKS_URL` (`MetroRail.Common/Auth.cs`).
ES256 replaced the Python version's EdDSA because the standard .NET JWT stack supports it natively;
security is equivalent.

**Consequences.** Fewer moving parts for the demo. Moving to Keycloak later only changes the
issuer/JWKS URL: services already verify standard JWTs. Staff (conductor) accounts with MFA will
need Keycloak or an equivalent before the scanner app ships.
