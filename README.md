# MetroRail

Live Cape Town Metrorail train tracking, delay alerts and tickets on your phone.
Concept for PRASA / Metrorail Western Cape — **not an official Metrorail app**.

```
apps/customer          React + Vite + TypeScript PWA, wrapped with Capacitor (Android / iOS)
services/*             9 ASP.NET Core (.NET 10) microservices, one database each, events over NATS
libs/MetroRail.Common  shared service template: settings, auth (JWT/JWKS), EF Core DB, events, errors, clock
tests/                 xUnit tests — every service runs in-process (SQLite + in-memory events)
tools/MetroRail.Tools  dev runner (no Docker), train simulator, end-to-end smoke, QR interop, OpenAPI export
contracts/             OpenAPI (generated), AsyncAPI events, ticket QR spec
infra/                 docker-compose, Kong gateway, Centrifugo (live updates), Postgres, one Dockerfile
docs/                  ADRs, work split
```

## Services

| Service | What it does |
|---|---|
| `Network` | Stations, lines, timetables, departure boards, direct journeys (Southern line seeded) |
| `Tracking` | Live train positions → **moving / held (3+ min) / stopped (5+ min) / disruption (15+ min)**, delays, crowd reports |
| `Eta` | Predicted arrival at every stop with a likely range (baseline now; LightGBM via ONNX Runtime when a model is provided) |
| `Realtime` | Pushes live train updates + alerts to phones through Centrifugo (SSE) |
| `Identity` | Phone/email 6-digit code login, ES256 JWTs + JWKS, consents, POPIA export/delete |
| `Trips` | Saved trips (name, stations, days, usual train, alert settings) |
| `Notifications` | In-app alerts when a saved trip's train is late, held or stopped |
| `Ticketing` | Products, purchases, signed tickets, sealed rotating QR, validation, trip counting |
| `Billing` | Card-only payments via Pay@ (mock now), 3-D Secure, saved card tokens, receipts |

## Run it

Prereqs: [.NET 10 SDK](https://dotnet.microsoft.com/download), Node 20+, and either Docker **or** `nats-server`.

```bash
make setup          # restore .NET + app packages
make test           # 48 xUnit tests

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

- **Where's my train:** simulator/GPS → `Tracking` (state + delay) → NATS → `Realtime` → phone (SSE);
  the app asks `Eta` for *"Your train reaches Rondebosch at 07:20 … You arrive at Claremont at 07:27"*.
- **Alerts:** `Trips` publishes saved trips → `Notifications` watches those trains → alert on delay /
  held / stopped → pushed live to the user's channel.
- **Tickets:** app creates a purchase (price set by `Ticketing`) → `Billing` takes the card payment →
  `Ticketing` issues a certificate bound to the phone's key → app shows a QR that changes every 30 s,
  sealed so only MetroRail scanners can read it, usable once, one trip per journey. Spec:
  [`contracts/ticket-qr.md`](contracts/ticket-qr.md).

## Configuration
Copy `infra/.env.example` to `infra/.env`. Never commit secrets (Pay@ sandbox keys go in `.env`).
Production requires `IDENTITY_SIGNING_KEY`, `TICKETING_ISSUER_KEY`, `TICKETING_SCANNER_KEY`, real
`PAYAT_*` values and no `DEMO_TIME`; services refuse to start in production with mocks.

See [`docs/WORK-SPLIT.md`](docs/WORK-SPLIT.md) for who owns what and the next tasks, and
[`docs/adr/0004-csharp-dotnet-backend.md`](docs/adr/0004-csharp-dotnet-backend.md) for why the backend is C#.
