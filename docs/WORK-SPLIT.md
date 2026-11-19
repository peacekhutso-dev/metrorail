# Who owns what (MVP → 16 Dec 2026)

| Area | Owner | Folder |
|---|---|---|
| Customer app (screens, live updates, ticket QR, Capacitor build) | Peace | `apps/customer` |
| Billing + Pay@ sandbox integration | Peace | `services/billing` |
| Network, tracking, ETA, realtime, notifications | Eyethu | `services/*` |
| Identity, trips, ticketing, contracts, infra, CI | Eyethu (Peace reviews ticketing) | `services/*`, `infra`, `contracts` |
| Security (POPIA, OWASP ASVS L2 checklist), threat model | Shared | `docs/` |

## Workflow
- Branch from `main`: `feat/<area>-<thing>`, e.g. `feat/billing-payat-sandbox`.
- Small PRs; the other person reviews. CI must be green (tests, lint, contracts, app build).
- Changing an API? Change the service, run `make contracts`, update `apps/customer/src/api`.

## Next tasks (already scaffolded, marked TODO in code)
1. **Pay@ sandbox** — implement `PayAtClient` (create, hosted card page, 3-D Secure, webhook signature). *(Peace)*
2. **Alembic migrations** per service (local/test use `create_all`; staging/prod need migrations). *(Eyethu)*
3. **Real map** on Live route — MapLibre GL + OpenStreetMap tiles instead of the schematic. *(Peace)*
4. **Northern + Cape Flats lines** and journeys with changes (e.g. via Mutual) in `network`. *(Eyethu)*
5. **Leave-now reminders** — scheduler in `notifications` using saved trip times. *(Eyethu)*
6. **SMS sender** for login codes (Clickatell/BulkSMS) in `identity/senders.py`. *(Eyethu)*
7. **Capacitor Android build** + privacy-screen plugin check on a real phone. *(Peace)*
8. **Scanner app** after MVP (`apps/scanner/README.md`).
