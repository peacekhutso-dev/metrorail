# Conductor scanner app (after MVP)

Separate app for PRASA staff — never mixed with the commuter app.

Plan (from the architecture spec):
- Capacitor (Android) + ZXing camera scanning, MFA staff sign-in, choose train for the shift.
- Validates tickets **offline**: port `read_qr` from `services/ticketing/metrorail_ticketing/crypto.py`
  (format: `contracts/ticket-qr.md`), keep a local nonce + trip-start log, sync every 60 s
  with `POST /api/ticketing/validations` / a batch sync endpoint (to add).
- Screens already designed in the mock-up: Staff sign-in, Start shift, Scan, Valid, Rejected, Shift summary.
- IDs: scanners `SC-000123`, staff `STF-…`, trains `TR-…`, stations `ST-…`.
