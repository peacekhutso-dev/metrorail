import jwt

from metrorail_common.testing import auth, client_for
from metrorail_realtime.centrifugo import FakeCentrifugo
from metrorail_realtime.main import Settings, build_app


async def test_token_channels_guest_and_user():
    st = Settings(environment="test")
    app = build_app(st, centrifugo=FakeCentrifugo())
    async with client_for(app) as c:
        guest = (await c.get("/token", params={"trains": "3412"})).json()
        assert guest["channels"] == ["trains:3412"]
        user = (await c.get("/token", params={"trains": "3412,3420"},
                            headers=auth("u1"))).json()
        claims = jwt.decode(user["token"], st.centrifugo_token_secret, algorithms=["HS256"])
        assert claims["channels"] == ["trains:3412", "trains:3420", "alerts:#u1"]


async def test_events_are_forwarded_to_channels():
    fake = FakeCentrifugo()
    app = build_app(Settings(environment="test"), centrifugo=fake)
    async with client_for(app):
        await app.state.bus.publish("tracking.train.state_changed",
                                    {"train_no": "3412", "state": "stopped", "stationary_s": 310})
        await app.state.bus.publish("notifications.alert.created", {
            "user_id": "u1", "id": "ALR-1", "kind": "stopped", "title": "t", "body": "b",
            "train_no": "3412"})
        assert fake.sent[0][0] == "trains:3412" and fake.sent[0][1]["state"] == "stopped"
        assert fake.sent[1][0] == "alerts:#u1"
