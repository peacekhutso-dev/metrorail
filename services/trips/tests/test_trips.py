from metrorail_common.testing import auth, client_for
from metrorail_trips.main import Settings, build_app

WORK = {"name": "Work", "from_station_id": "ST-RONDEBOSCH", "to_station_id": "ST-CLAREMONT",
        "days": ["Mon", "Tue", "Wed", "Thu", "Fri"], "usual_time": "07:18", "train_no": "3412"}


async def test_add_edit_list_delete_trip():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        h = auth("u1")
        r = await c.post("/trips", json=WORK, headers=h)
        assert r.status_code == 201, r.text
        tid = r.json()["id"]
        r = await c.put(f"/trips/{tid}", json={**WORK, "name": "Varsity"}, headers=h)
        assert r.json()["name"] == "Varsity"
        assert [t["name"] for t in (await c.get("/trips", headers=h)).json()] == ["Varsity"]
        # other users can't see or touch it
        assert (await c.get("/trips", headers=auth("u2"))).json() == []
        assert (await c.delete(f"/trips/{tid}", headers=auth("u2"))).status_code == 404
        assert (await c.delete(f"/trips/{tid}", headers=h)).status_code == 204
        subjects = [e for e, _ in app.state.bus.published]
        assert subjects == ["trips.trip.saved", "trips.trip.saved", "trips.trip.deleted"]


async def test_validation_and_login_required():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        assert (await c.get("/trips")).status_code == 401
        r = await c.post("/trips", json={**WORK, "to_station_id": "ST-RONDEBOSCH"}, headers=auth())
        assert r.json()["error"]["code"] == "same_station"
        r = await c.post("/trips", json={**WORK, "name": "   "}, headers=auth())
        assert r.status_code == 422


async def test_user_deleted_purges_trips():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        await c.post("/trips", json=WORK, headers=auth("u9"))
        await app.state.bus.publish("identity.user.deleted", {"user_id": "u9"})
        assert (await c.get("/trips", headers=auth("u9"))).json() == []
