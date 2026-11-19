import time

from metrorail_common.testing import auth, client_for
from metrorail_ticketing.crypto import build_qr, device_keypair
from metrorail_ticketing.main import Settings, build_app
from metrorail_ticketing.products import journey_window_min

CONDUCTOR = auth("staff-1", "conductor")


async def buy(c, app, product="ten_trip", user="u1"):
    key, dpk = device_keypair()
    r = await c.post("/purchases", headers=auth(user), json={
        "product_id": product, "from_station_id": "ST-RONDEBOSCH",
        "to_station_id": "ST-CLAREMONT", "device_public_key": dpk})
    assert r.status_code == 201, r.text
    pid = r.json()["id"]
    assert ("ticketing.purchase.created" in [e for e, _ in app.state.bus.published])
    await app.state.bus.publish("billing.payment.succeeded", {"purchase_id": pid})
    p = (await c.get(f"/purchases/{pid}", headers=auth(user))).json()
    assert p["status"] == "paid"
    t = (await c.get(f"/tickets/{p['ticket_id']}", headers=auth(user))).json()
    return t, key


async def scan(c, app, t, key, now=None, train="3412"):
    qr = build_qr(t["cert"], key, app.state.keys.public()["scanner_x25519"], now=now)
    return (await c.post("/validations", headers=CONDUCTOR, json={
        "qr": qr, "train_no": train, "scanner_id": "SC-000123"})).json(), qr


def test_journey_window():
    assert journey_window_min(7) == 90          # short trip -> minimum 90 min
    assert journey_window_min(60, changes=1) == 120
    assert journey_window_min(200, changes=2) == 240  # capped at 4 h


async def test_ten_trip_one_trip_per_journey_not_per_scan():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        t, key = await buy(c, app)
        assert t["trips_total"] == 10 and t["status"] == "ready"
        v1, _ = await scan(c, app, t, key)
        assert v1["result"] == "valid" and v1["new_trip_started"] and v1["trip_no"] == 1
        v2, _ = await scan(c, app, t, key)  # second conductor, same journey
        assert v2["result"] == "valid" and not v2["new_trip_started"] and v2["trips_left"] == 9
        mine = (await c.get(f"/tickets/{t['id']}", headers=auth("u1"))).json()
        assert mine["status"] == "in_progress" and mine["trips_left"] == 9


async def test_same_code_twice_is_rejected():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        t, key = await buy(c, app)
        _, qr = await scan(c, app, t, key)
        r = (await c.post("/validations", headers=CONDUCTOR, json={
            "qr": qr, "train_no": "3412", "scanner_id": "SC-000124"})).json()
        assert r == {**r, "result": "rejected", "reason": "already_scanned"}


async def test_old_code_photo_is_rejected():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        t, key = await buy(c, app)
        v, _ = await scan(c, app, t, key, now=time.time() - 120)  # code from 2 minutes ago
        assert v["reason"] == "expired_code"


async def test_ticket_on_another_phone_is_rejected():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        t, _ = await buy(c, app)
        other_phone, _ = device_keypair()
        v, _ = await scan(c, app, t, other_phone)
        assert v["reason"] == "not_this_phone"


async def test_garbage_and_non_metrorail_codes():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        for qr, reason in [("https://example.com", "not_a_metrorail_code"),
                           ("MR1:AAAA", "invalid_code")]:
            r = (await c.post("/validations", headers=CONDUCTOR, json={
                "qr": qr, "train_no": "3412", "scanner_id": "SC-000123"})).json()
            assert r["reason"] == reason


async def test_single_trip_used_up_then_no_trips_left():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        t, key = await buy(c, app, product="single")
        assert (await scan(c, app, t, key))[0]["result"] == "valid"
        # close the journey window to simulate a later journey
        from metrorail_ticketing.models import Ticket
        async with app.state.db.sessionmaker() as s:
            row = await s.get(Ticket, t["id"])
            row.open_trip_until = None
            await s.commit()
        assert (await scan(c, app, t, key))[0]["reason"] == "no_trips_left"


async def test_topup_adds_trips_and_reissues_cert():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        t, key = await buy(c, app)
        r = await c.post(f"/tickets/{t['id']}/topup", json={"trips": 10}, headers=auth("u1"))
        assert r.json()["amount_cents"] == 9500
        await app.state.bus.publish("billing.payment.succeeded", {"purchase_id": r.json()["id"]})
        t2 = (await c.get(f"/tickets/{t['id']}", headers=auth("u1"))).json()
        assert t2["trips_total"] == 20 and t2["cert"] != t["cert"]
        assert (await scan(c, app, t, key))[0]["reason"] == "replaced_ticket"  # old cert
        assert (await scan(c, app, t2, key))[0]["result"] == "valid"


async def test_only_conductors_can_validate():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        r = await c.post("/validations", headers=auth("u1"), json={
            "qr": "MR1:x", "train_no": "3412", "scanner_id": "SC-000123"})
        assert r.status_code == 403
