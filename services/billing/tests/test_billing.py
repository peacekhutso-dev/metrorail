import hashlib
import hmac
import json

from metrorail_billing.main import Settings, build_app
from metrorail_billing.payat import luhn_ok
from metrorail_common.testing import auth, client_for

CHARGE = {"purchase_id": "PUR-1", "user_id": "u1", "amount_cents": 1200, "description": "Single trip"}
GOOD = {"number": "4000 0000 0000 0002", "expiry": "08/29", "cvv": "123", "name": "P Molimo"}


def test_luhn():
    assert luhn_ok("4000000000000002") and not luhn_ok("4000000000000001")


async def setup(c, app, purchase="PUR-1"):
    await app.state.bus.publish("ticketing.purchase.created", {**CHARGE, "purchase_id": purchase})
    r = await c.post("/payments", headers=auth("u1"),
                     json={"purchase_id": purchase, "method": "new_card"})
    assert r.status_code == 201, r.text
    return r.json()


async def test_new_card_3ds_success_saves_card_and_emits_event():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        p = await setup(c, app)
        assert p["status"] == "requires_card" and p["amount_cents"] == 1200
        r = (await c.post(f"/payments/{p['id']}/mock-card", headers=auth("u1"),
                          json={**GOOD, "save": True})).json()
        assert r["status"] == "requires_3ds" and r["last4"] == "0002" and r["brand"] == "visa"
        r = (await c.post(f"/payments/{p['id']}/mock-3ds", headers=auth("u1"),
                          json={"otp": "123456"})).json()
        assert r["status"] == "succeeded"
        assert ("billing.payment.succeeded" in [e for e, _ in app.state.bus.published])
        cards = (await c.get("/cards", headers=auth("u1"))).json()
        assert cards[0]["last4"] == "0002"
        # saved card -> next payment goes straight to bank approval
        await app.state.bus.publish("ticketing.purchase.created", {**CHARGE, "purchase_id": "PUR-2"})
        r = (await c.post("/payments", headers=auth("u1"), json={
            "purchase_id": "PUR-2", "method": "saved_card", "saved_card_id": cards[0]["id"]})).json()
        assert r["status"] == "requires_3ds"


async def test_declined_and_bad_card():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        p = await setup(c, app)
        r = (await c.post(f"/payments/{p['id']}/mock-card", headers=auth("u1"),
                          json={**GOOD, "number": "4000 0000 0000 0001"})).json()
        assert r["decline_reason"] == "invalid_card_number"
        await c.post(f"/payments/{p['id']}/mock-card", headers=auth("u1"),
                     json={**GOOD, "number": "4000 0000 0000 0069"})
        r = (await c.post(f"/payments/{p['id']}/mock-3ds", headers=auth("u1"),
                          json={"otp": "123456"})).json()
        assert r["status"] == "declined" and r["decline_reason"] == "declined_by_bank"
        assert ("billing.payment.failed" in [e for e, _ in app.state.bus.published])


async def test_cannot_pay_someone_elses_purchase():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        await app.state.bus.publish("ticketing.purchase.created", CHARGE)
        r = await c.post("/payments", headers=auth("u2"),
                         json={"purchase_id": "PUR-1", "method": "new_card"})
        assert r.status_code == 404


async def test_webhook_signature_and_idempotency():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        p = await setup(c, app)
        ref = app.state.gateway  # find gateway ref via payment record
        from metrorail_billing.models import Payment
        async with app.state.db.sessionmaker() as s:
            gref = (await s.get(Payment, p["id"])).gateway_ref
        body = json.dumps({"gateway_ref": gref, "status": "succeeded"}).encode()
        sig = hmac.new(b"mock-secret", body, hashlib.sha256).hexdigest()
        assert (await c.post("/webhooks/payat", content=body,
                             headers={"x-payat-signature": "bad"})).status_code == 401
        for _ in range(2):
            assert (await c.post("/webhooks/payat", content=body,
                                 headers={"x-payat-signature": sig})).status_code == 204
        events = [e for e, _ in app.state.bus.published if e == "billing.payment.succeeded"]
        assert len(events) == 1 and ref is not None
