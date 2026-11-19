"""End-to-end smoke test against a running stack (docker-compose or tools/dev/run_all.py).

    DEMO_TIME=07:14 ...start the stack...
    uv run python tools/e2e/smoke.py

Walks the real commuter journey through the gateway: log in with a code, save a trip,
train 3412 runs late then stops, ETA moves, an alert arrives, buy a 10-trip ticket with a card,
and a conductor scans the rotating QR (and a re-used code is refused).
"""

import asyncio
import os
import sys

import httpx

from metrorail_ticketing.crypto import build_qr, device_keypair

API = os.environ.get("API", "http://localhost:8080/api")
FEED = {"Authorization": "Bearer dev:simulator:feed"}
CONDUCTOR = {"Authorization": "Bearer dev:staff-1:conductor"}


def step(msg):
    print(f"  ✓ {msg}")


async def main() -> None:
    async with httpx.AsyncClient(base_url=API, timeout=10) as c:
        print("Login")
        start = (await c.post("/identity/auth/start", json={"phone": "082 456 4567"})).json()
        tok = (await c.post("/identity/auth/verify", json={
            "challenge_id": start["challenge_id"], "code": start["dev_code"]})).json()
        me = {"Authorization": f"Bearer {tok['access_token']}"}
        step(f"code sent to {start['sent_to']}, user {tok['user_id']} (real EdDSA token)")
        assert (await c.get("/identity/me", headers=me)).status_code == 200
        step("token accepted by identity")

        print("Saved trip")
        trip = (await c.post("/trips/trips", headers=me, json={
            "name": "Work", "from_station_id": "ST-RONDEBOSCH", "to_station_id": "ST-CLAREMONT",
            "days": ["Mon", "Tue", "Wed", "Thu", "Fri"], "usual_time": "07:18",
            "train_no": "3412"})).json()
        step(f"{trip['name']}: Rondebosch → Claremont, train {trip['train_no']} (token verified via JWKS)")
        await asyncio.sleep(0.5)  # let notifications receive trips.trip.saved over NATS

        print("Train 3412 live")
        st = (await c.get("/network/stations")).json()
        pos = {s["id"]: s for s in st}
        mid = {"lat": (pos["ST-MOWBRAY"]["lat"] + pos["ST-ROSEBANK"]["lat"]) / 2,
               "lng": (pos["ST-MOWBRAY"]["lng"] + pos["ST-ROSEBANK"]["lng"]) / 2}
        r = (await c.post("/tracking/positions", headers=FEED,
                          json={"train_no": "3412", **pos["ST-MOWBRAY"] | {}, "source": "simulator",
                                "lat": pos["ST-MOWBRAY"]["lat"], "lng": pos["ST-MOWBRAY"]["lng"]})).json()
        step(f"at Mowbray: state={r['state']} delay={r['delay_min']} min")
        r = (await c.post("/tracking/positions", headers=FEED,
                          json={"train_no": "3412", **mid, "source": "simulator"})).json()
        j = (await c.get("/eta/trains/3412/journey",
                         params={"board": "ST-RONDEBOSCH", "alight": "ST-CLAREMONT"})).json()
        step(f"moving: reaches Rondebosch {j['board_predicted']} (sched {j['board_scheduled']}), "
             f"Claremont {j['alight_predicted']}, {j['stops_until_board']} stops away")
        moving_eta = j["board_predicted"]

        # Pretend it has been standing still for 6 minutes (stopped)
        from datetime import UTC, datetime, timedelta
        seen = datetime.fromisoformat(r["seen_at"]).astimezone(UTC)
        r = (await c.post("/tracking/positions", headers=FEED, json={
            "train_no": "3412", **mid, "source": "simulator",
            "at": (seen + timedelta(minutes=6)).isoformat()})).json()
        assert r["state"] == "stopped", r
        step(f"6 min without moving -> state={r['state']}")
        await asyncio.sleep(0.5)
        alerts = (await c.get("/notifications/alerts", headers=me)).json()
        assert alerts and alerts[0]["kind"] == "stopped", alerts
        step(f"alert: '{alerts[0]['title']}' — {alerts[0]['body']}")
        j = (await c.get("/eta/trains/3412/journey",
                         params={"board": "ST-RONDEBOSCH", "alight": "ST-CLAREMONT"})).json()
        step(f"stopped: ETA moved {moving_eta} -> {j['board_predicted']} "
             f"(likely {j['likely_from']}–{j['likely_to']})")

        print("Buy a 10-trip ticket by card")
        key, dpk = device_keypair()
        p = (await c.post("/ticketing/purchases", headers=me, json={
            "product_id": "ten_trip", "from_station_id": "ST-RONDEBOSCH",
            "to_station_id": "ST-CLAREMONT", "device_public_key": dpk})).json()
        step(f"{p['description']} R{p['amount_cents'] / 100:.2f}")
        await asyncio.sleep(0.5)  # billing learns the amount from ticketing.purchase.created
        pay = (await c.post("/billing/payments", headers=me,
                            json={"purchase_id": p["id"], "method": "new_card"})).json()
        pay = (await c.post(f"/billing/payments/{pay['id']}/mock-card", headers=me, json={
            "number": "4000 0000 0000 0002", "expiry": "08/29", "cvv": "123", "name": "P Molimo",
            "save": True})).json()
        step(f"card {pay['brand']} •••• {pay['last4']} -> {pay['status']}")
        pay = (await c.post(f"/billing/payments/{pay['id']}/mock-3ds", headers=me,
                            json={"otp": "123456"})).json()
        step(f"3-D Secure -> {pay['status']}")
        await asyncio.sleep(0.5)
        p = (await c.get(f"/ticketing/purchases/{p['id']}", headers=me)).json()
        t = (await c.get(f"/ticketing/tickets/{p['ticket_id']}", headers=me)).json()
        step(f"ticket {t['id']} issued: {t['trips_left']} of {t['trips_total']} trips")

        print("Conductor scans")
        keys = (await c.get("/ticketing/keys")).json()
        qr = build_qr(t["cert"], key, keys["scanner_x25519"])
        v = (await c.post("/ticketing/validations", headers=CONDUCTOR, json={
            "qr": qr, "train_no": "3412", "scanner_id": "SC-000123"})).json()
        assert v["result"] == "valid", v
        step(f"VALID trip {v['trip_no']} of 10, {v['trips_left']} left")
        v = (await c.post("/ticketing/validations", headers=CONDUCTOR, json={
            "qr": qr, "train_no": "3412", "scanner_id": "SC-000124"})).json()
        assert v["reason"] == "already_scanned", v
        step("same code again -> REJECTED (already_scanned)")
        qr2 = build_qr(t["cert"], key, keys["scanner_x25519"])
        v = (await c.post("/ticketing/validations", headers=CONDUCTOR, json={
            "qr": qr2, "train_no": "3412", "scanner_id": "SC-000124"})).json()
        assert v["result"] == "valid" and not v["new_trip_started"], v
        step("second conductor, fresh code, same journey -> VALID, no extra trip used")

        print("POPIA")
        exp = (await c.get("/identity/me/export", headers=me)).json()
        step(f"export: {list(exp['account'])}")
    print("\nAll good.")


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except AssertionError as e:
        print("FAILED:", e)
        sys.exit(1)
