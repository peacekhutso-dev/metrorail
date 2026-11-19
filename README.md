# MetroRail

Live Cape Town Metrorail train tracking, delay alerts and tickets on your phone.
Concept for PRASA / Metrorail Western Cape — **not an official Metrorail app**.

```
apps/customer      React + Vite + TypeScript PWA, wrapped with Capacitor (Android / iOS)
services/*         9 FastAPI microservices, one database each, events over NATS
libs/py-common     shared service template: config, auth (JWT/JWKS), DB, events, errors, logging
contracts/         OpenAPI (generated), AsyncAPI events, ticket QR spec
infra/             docker-compose, Kong gateway, Centrifugo (live updates), Postgres
tools/             dev runner (no Docker), train simulator, end-to-end smoke tests
docs/              ADRs, work split
```

## Services

| Service | What it does |
|---|---|
| `network` | Stations, lines, timetables, departure boards, direct journeys (Southern line seeded) |
| `tracking` | Live train positions → **moving / held (3+ min) / stopped (5+ min) / disruption (15+ min)**, delays, crowd reports |
| `eta` | Predicted arrival at every stop with a likely range (baseline now, LightGBM via MLflow later) |
| `realtime` | Pushes live train updates + alerts to phones through Centrifugo (SSE) |
| `identity` | Phone/email 6-digit code login, Ed25519 JWTs + JWKS, consents, POPIA export/delete |
| `trips` | Saved trips (name, stations, days, usual train, alert settings) |
| `notifications` | In-app alerts when a saved trip's train is late, held or stopped |
| `ticketing` | Products, purchases, signed tickets, sealed rotating QR, validation, trip counting |
| `billing` | Card-only payments via Pay@ (mock now), 3-D Secure, saved card tokens, receipts |

## Run it

Prereqs: [uv](https://docs.astral.sh/uv/), Node 20+, and either Docker **or** `nats-server`.

```bash
make setup          # Python + app dependencies
make test           # 40 service tests

# Option A — Docker (Postgres, NATS, Kong, Centrifugo, all services)
make up             # gateway: http://localhost:8080/api/<service>/docs

# Option B — no Docker (SQLite files + local nats-server)
make dev            # same gateway on :8080, DEMO_TIME=07:14

make app            # customer app: http://localhost:5173
make sim            # move train 3412   (make sim MODE=stopped)
make smoke          # full journey end-to-end + app↔server QR check
```

Demo login: any SA number, e.g. `082 456 4567` — the code is shown on screen in non-production
builds. Demo card: `4000 0000 0000 0002` (approved), `4000 0000 0000 0069` (declined); bank PIN `123456`.

## How the main flows work

- **Where's my train:** simulator/GPS → `tracking` (state + delay) → NATS → `realtime` → phone (SSE);
  the app asks `eta` for *"Your train reaches Rondebosch at 07:20 … You arrive at Claremont at 07:27"*.
- **Alerts:** `trips` publishes saved trips → `notifications` watches those trains → alert on delay /
  held / stopped → pushed live to the user's channel.
- **Tickets:** app creates a purchase (price set by `ticketing`) → `billing` takes the card payment →
  `ticketing` issues a certificate bound to the phone's key → app shows a QR that changes every 30 s,
  sealed so only MetroRail scanners can read it, usable once, one trip per journey. Spec:
  [`contracts/ticket-qr.md`](contracts/ticket-qr.md).

## Configuration
Copy `infra/.env.example` to `infra/.env`. Never commit secrets (Pay@ sandbox keys go in `.env`).
Production requires `IDENTITY_SIGNING_KEY`, `TICKETING_ISSUER_KEY`, `TICKETING_SCANNER_KEY`, real
`PAYAT_*` values and no `DEMO_TIME`; services refuse to start in production with mocks.

See [`docs/WORK-SPLIT.md`](docs/WORK-SPLIT.md) for who owns what and the next tasks.
