from datetime import UTC, datetime, timedelta

from metrorail_common.testing import auth, client_for
from metrorail_tracking.main import Settings, build_app
from metrorail_tracking.state import TrainState, classify

FEED = auth("feed-1", "feed")


class FakeNetwork:
    """Minimal network service: train 3412, Mowbray -> Rosebank -> Rondebosch."""

    async def get(self, path, **_):
        if path == "/stations":
            return [
                {"id": "ST-MOWBRAY", "lat": -33.9466, "lng": 18.4740},
                {"id": "ST-ROSEBANK", "lat": -33.9543, "lng": 18.4734},
                {"id": "ST-RONDEBOSCH", "lat": -33.9612, "lng": 18.4705},
            ]
        return {"train_no": "3412", "stops": [
            {"station_id": "ST-MOWBRAY", "scheduled": "07:13"},
            {"station_id": "ST-ROSEBANK", "scheduled": "07:15"},
            {"station_id": "ST-RONDEBOSCH", "scheduled": "07:18"},
        ]}


def test_thresholds():
    assert classify(60, at_station=True) == TrainState.AT_STATION
    assert classify(130, at_station=False) == TrainState.HELD     # 2+ min between stations
    assert classify(130, at_station=True) == TrainState.AT_STATION
    assert classify(200, at_station=True) == TrainState.HELD      # 3+ min at a station
    assert classify(300, at_station=False) == TrainState.STOPPED  # 5+ min
    assert classify(900, at_station=False) == TrainState.DISRUPTION


async def test_train_goes_held_then_stopped_between_stations():
    app = build_app(Settings(environment="test"), network=FakeNetwork())
    t0 = datetime(2026, 11, 20, 5, 14, tzinfo=UTC)  # 07:14 SAST
    between = {"train_no": "3412", "lat": -33.9500, "lng": 18.4737}  # Mowbray-Rosebank
    async with client_for(app) as c:
        r = await c.post("/positions", json={**between, "at": t0.isoformat()}, headers=FEED)
        assert r.status_code == 200, r.text
        assert r.json()["state"] == "moving"
        assert r.json()["last_station_id"] == "ST-MOWBRAY"
        assert r.json()["next_station_id"] == "ST-ROSEBANK"

        r = await c.post("/positions", headers=FEED,
                         json={**between, "at": (t0 + timedelta(seconds=150)).isoformat()})
        assert r.json()["state"] == "held"

        r = await c.post("/positions", headers=FEED,
                         json={**between, "at": (t0 + timedelta(seconds=310)).isoformat()})
        assert r.json()["state"] == "stopped"

        events = [e for e, _ in app.state.bus.published if e == "tracking.train.state_changed"]
        assert len(events) == 3  # moving, held, stopped


async def test_delay_recorded_when_train_reaches_station():
    app = build_app(Settings(environment="test"), network=FakeNetwork())
    at = datetime(2026, 11, 20, 5, 17, tzinfo=UTC)  # 07:17 SAST at Rosebank, scheduled 07:15
    async with client_for(app) as c:
        r = await c.post("/positions", headers=FEED, json={
            "train_no": "3412", "lat": -33.9543, "lng": 18.4734, "at": at.isoformat()})
        assert r.json()["at_station_id"] == "ST-ROSEBANK"
        assert r.json()["delay_min"] == 2
        assert r.json()["next_station_id"] == "ST-RONDEBOSCH"


async def test_positions_require_feed_role():
    app = build_app(Settings(environment="test"), network=FakeNetwork())
    async with client_for(app) as c:
        r = await c.post("/positions", json={"train_no": "3412", "lat": 0, "lng": 0},
                         headers=auth("someone"))
        assert r.status_code == 403
