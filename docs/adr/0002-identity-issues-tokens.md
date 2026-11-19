# ADR 0002 — Identity service issues its own tokens for the MVP (Keycloak later)

**Status:** accepted for MVP

**Context.** The architecture spec names Keycloak (OIDC, passwordless). For the MVP we need phone/
email code login, short-lived tokens every service can verify offline, and POPIA export/delete.

**Decision.** `services/identity` issues Ed25519-signed JWTs (15 min) + rotating refresh tokens
(30 days) and publishes a JWKS. All services verify via `JWKS_URL` (`metrorail_common.auth`).

**Consequences.** Fewer moving parts for the demo. Moving to Keycloak later only changes the
issuer/JWKS URL: services already verify standard JWTs. Staff (conductor) accounts with MFA will
need Keycloak or an equivalent before the scanner app ships.
