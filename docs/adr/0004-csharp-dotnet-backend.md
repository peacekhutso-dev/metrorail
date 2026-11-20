# ADR 0004 — Backend in C# / .NET 10 (replacing Python/FastAPI)

**Status:** accepted (20 Nov 2026)

**Context.** The first backend was Python/FastAPI. The team wants better per-server throughput at
PRASA scale, and the backend owner works in C#.

**Decision.** All nine services are ASP.NET Core 10 (minimal APIs) on .NET 10 LTS:
EF Core (Npgsql for PostgreSQL, SQLite for tests/dev), NATS.Net for events, Microsoft.IdentityModel
for JWT, BouncyCastle for Ed25519/X25519 (ticket certificate + QR sealing), built-in .NET crypto for
ECDSA, HKDF and AES-GCM, ONNX Runtime for the ETA model. The HTTP API, JSON field names, events and
ticket QR format are unchanged, so the customer app did not change.

**What stays in another language.**
- Customer app: TypeScript (React + Capacitor).
- ETA model *training* is offline data-science tooling in Python (`tools/ml/train_eta.py`); it
  exports ONNX, which the C# eta service runs. Nothing Python runs in production.

**Consequences.** Compiled, multi-threaded services with lower memory and CPU per request than
CPython; one language for the whole backend; strong typing across services. The Python version is
in git history (commit 5dd39aa) if anything needs to be compared.
