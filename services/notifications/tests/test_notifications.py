from metrorail_common.testing import auth, client_for
from metrorail_notifications.main import Settings, build_app

TRIP = {"id": "TRP-1", "user_id": "u1", "name": "Work", "train_no": "3412", "alert_delays": True,
        "from_station_id": "ST-RONDEBOSCH", "usual_time": "07:18"}


async def test_stopped_train_alerts_trip_owner_once():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        bus = app.state.bus
        await bus.publish("trips.trip.saved", TRIP)
        ev = {"train_no": "3412", "state": "stopped", "stationary_s": 437, "delay_min": 2,
              "last_station_id": "ST-MOWBRAY", "next_station_id": "ST-ROSEBANK"}
        await bus.publish("tracking.train.state_changed", ev)
        await bus.publish("tracking.train.state_changed", ev)  # duplicate -> no second alert
        alerts = (await c.get("/alerts", headers=auth("u1"))).json()
        assert len(alerts) == 1
        assert alerts[0]["title"] == "Your train has stopped"
        assert "between Mowbray and Rosebank for 7 min" in alerts[0]["body"]
        assert (await c.get("/alerts/unread-count", headers=auth("u1"))).json()["unread"] == 1
        await c.post(f"/alerts/{alerts[0]['id']}/read", headers=auth("u1"))
        assert (await c.get("/alerts/unread-count", headers=auth("u1"))).json()["unread"] == 0
        assert any(s == "notifications.alert.created" for s, _ in bus.published)


async def test_no_alert_for_other_trains_or_on_time():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        await app.state.bus.publish("trips.trip.saved", TRIP)
        await app.state.bus.publish("tracking.train.state_changed",
                                    {"train_no": "3416", "state": "stopped", "stationary_s": 400})
        await app.state.bus.publish("tracking.train.state_changed",
                                    {"train_no": "3412", "state": "moving", "delay_min": 0})
        assert (await c.get("/alerts", headers=auth("u1"))).json() == []


async def test_alerts_off_means_no_watch():
    app = build_app(Settings(environment="test"))
    async with client_for(app) as c:
        await app.state.bus.publish("trips.trip.saved", {**TRIP, "alert_delays": False})
        await app.state.bus.publish("tracking.train.state_changed",
                                    {"train_no": "3412", "state": "held", "stationary_s": 200})
        assert (await c.get("/alerts", headers=auth("u1"))).json() == []
