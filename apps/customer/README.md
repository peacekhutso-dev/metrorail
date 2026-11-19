# MetroRail customer app

React + Vite + TypeScript, packaged for Android/iOS with Capacitor.

```bash
npm install
npm run dev          # http://localhost:5173 — /api is proxied to the gateway on :8080
npm run build        # production bundle in dist/
npx cap add android  # first time only, then: npm run build && npx cap sync && npx cap open android
```

| Folder | |
|---|---|
| `src/screens` | Welcome, Login (code), Consent, Home (My routes / guest), Plan, Departures, Live route, My trips / Add a trip, Tickets, Buy (checkout → card → 3-D Secure), Show ticket, Alerts, Account |
| `src/components` | TripCard (moving / held / stopped), StopStrip, TabBar, UI bits |
| `src/api` | typed API client (auto token refresh) + response types |
| `src/realtime` | live train updates over SSE (Centrifugo), polling fallback |
| `src/ticket` | phone key (non-extractable, IndexedDB), rotating sealed QR — see `contracts/ticket-qr.md` |
| `src/styles` | design tokens from the mock-up |

Times are always labelled in words ("Your train reaches Rondebosch at 07:20"). The ETA is
predicted by the `eta` service; the UI never says "ML".
