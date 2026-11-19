# ADR 0001 — Monorepo with domain microservices

**Status:** accepted (Nov 2026)

**Context.** Two developers, MVP demo on 16 Dec 2026, but the system must scale to PRASA volumes
(target 500k daily users, 100k live connections) and be maintainable by PRASA's engineers.

**Decision.** One repository; each domain is its own FastAPI service with its own database and
deployable image (`services/*`). Shared plumbing lives in `libs/py-common` so every service
looks the same. Services talk through REST (sync reads) and NATS events (everything else).
Contracts are generated from code into `contracts/` and checked in CI.

**Consequences.** One PR can change a contract, a service and the app together. Services can be
split into separate repos later without code changes. More moving parts locally — mitigated by
`make dev` (no Docker) and `make up`.
