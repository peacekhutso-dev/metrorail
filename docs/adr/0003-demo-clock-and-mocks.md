# ADR 0003 — Demo clock, simulator and mock Pay@

**Status:** accepted

- `DEMO_TIME=07:14` makes all services behave as if it is the morning peak, so the pitch works at
  any hour (`IClock`). Security clocks (token expiry, QR 30-second slots) always use real time.
- `dotnet run --project tools/MetroRail.Tools -- sim` drives train 3412 (moving / held / stopped)
  until PRASA provides GPS or train location feeds.
- `PAYAT_MODE=mock` simulates Pay@ with test cards and 3-D Secure. The real `PayAtClient` is stubbed
  with TODOs to map to the Pay@ sandbox API (`services/Billing/PayAt.cs`).
- Fares are sample values (`services/Ticketing/Model.cs`, `Products`).
