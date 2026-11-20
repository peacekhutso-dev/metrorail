# Who owns what (MVP → 16 Dec 2026)

| Area | Owner | Folder |
|---|---|---|
| Customer app (screens, live updates, ticket QR, Capacitor build) | Peace | `apps/customer` |
| Billing + Pay@ sandbox integration | Peace | `services/Billing` |
| Network, tracking, ETA, realtime, notifications | Eyethu | `services/*` |
| Identity, trips, ticketing, contracts, infra, CI | Eyethu (Peace reviews ticketing) | `services/*`, `infra`, `contracts` |
| Security (POPIA, OWASP ASVS L2 checklist), threat model | Shared | `docs/` |

## Workflow
- Branch from `main`: `feat/<area>-<thing>`, e.g. `feat/billing-payat-sandbox`.
- Small PRs; the other person reviews. CI must be green (tests, lint, contracts, app build).
- Changing an API? Change the service, run `make contracts`, update `apps/customer/src/api`.
- Tests: `dotnet test` (xUnit, every service runs in-process with SQLite + in-memory events).

## Next tasks (already scaffolded, marked TODO in code)
1. **Pay@ sandbox** — implement `PayAtClient` in `services/Billing/PayAt.cs` (create, hosted card page, 3-D Secure, webhook signature). *(Peace)*
2. **EF Core migrations** per service (`dotnet ef migrations add Initial`; local/test use `EnsureCreated`, production needs migrations). *(Eyethu)*
3. **Real map** on Live route — MapLibre GL + OpenStreetMap tiles instead of the schematic. *(Peace)*
4. **Northern + Cape Flats lines** and journeys with changes (e.g. via Mutual) in `services/Network`. *(Eyethu)*
5. **Leave-now reminders** — a hosted background service in `services/Notifications` using saved trip times. *(Eyethu)*
6. **SMS sender** for login codes (Clickatell/BulkSMS): implement `ICodeSender` in `services/Identity/Tokens.cs`. *(Eyethu)*
7. **Capacitor Android build** + privacy-screen plugin check on a real phone. *(Peace)*
8. **Scanner app** after MVP (`apps/scanner/README.md`).
