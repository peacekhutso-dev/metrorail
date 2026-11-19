from metrorail_common.auth import verify_token
from metrorail_common.config import ServiceSettings
from metrorail_common.testing import client_for
from metrorail_identity.main import Settings, build_app


async def login(c, phone="082 456 4567"):
    start = (await c.post("/auth/start", json={"phone": phone})).json()
    r = await c.post("/auth/verify", json={"challenge_id": start["challenge_id"],
                                           "code": start["dev_code"]})
    return start, r


async def test_phone_login_issues_verifiable_token():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        start, r = await login(c)
        assert start["sent_to"] == "+27 82 *** 4567"
        assert r.status_code == 200, r.text
        tok = r.json()
        assert tok["is_new_user"] is True
        # any other service can verify it with just the public key
        other = ServiceSettings(jwt_public_key=app.state.signer.public_pem, allow_dev_tokens=False)
        assert verify_token(tok["access_token"], other).user_id == tok["user_id"]
        # second login = same user
        _, r2 = await login(c)
        assert r2.json()["user_id"] == tok["user_id"] and r2.json()["is_new_user"] is False


async def test_wrong_code_counts_down_then_locks():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        start = (await c.post("/auth/start", json={"phone": "0824564567"})).json()
        wrong = "000000" if start["dev_code"] != "000000" else "111111"
        msgs = []
        for _ in range(4):
            r = await c.post("/auth/verify", json={"challenge_id": start["challenge_id"],
                                                   "code": wrong})
            msgs.append(r.json()["error"]["code"])
        assert msgs == ["code_mismatch", "code_mismatch", "code_mismatch", "too_many_attempts"]


async def test_me_consents_refresh_and_delete():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        _, r = await login(c)
        tok = r.json()
        h = {"Authorization": f"Bearer dev:{tok['user_id']}"}
        me = (await c.get("/me", headers=h)).json()
        assert me["consents"]["nearest_station"] is False
        r = await c.put("/me/consents", headers=h,
                        json={"trip_alerts": True, "nearest_station": True, "usage_counts": False})
        assert r.json()["consents"]["nearest_station"] is True
        r = await c.post("/auth/refresh", json={"refresh_token": tok["refresh_token"]})
        assert r.status_code == 200
        # old refresh token is rotated out
        r = await c.post("/auth/refresh", json={"refresh_token": tok["refresh_token"]})
        assert r.status_code == 401
        assert (await c.delete("/me", headers=h)).status_code == 204
        assert ("identity.user.deleted", {"user_id": tok["user_id"]}) in app.state.bus.published
        assert (await c.get("/me", headers=h)).status_code == 404


async def test_jwks_published():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        k = (await c.get("/.well-known/jwks.json")).json()["keys"][0]
        assert k["kty"] == "OKP" and k["alg"] == "EdDSA"
