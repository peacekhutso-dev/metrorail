from metrorail_common.errors import NotFound
from metrorail_common.testing import client_for
from metrorail_eta.main import Settings, build_app
from metrorail_eta.predictor import BaselinePredictor, LiveIn, StopIn

STOPS = [StopIn("ST-MOWBRAY", "Mowbray", 7 * 60 + 13), StopIn("ST-ROSEBANK", "Rosebank", 7 * 60 + 15),
         StopIn("ST-RONDEBOSCH", "Rondebosch", 7 * 60 + 18),
         StopIn("ST-NEWLANDS", "Newlands", 7 * 60 + 21),
         StopIn("ST-CLAREMONT", "Claremont", 7 * 60 + 25)]
NOW = 7 * 60 + 14


def by_id(pred):
    return {s.station_id: s for s in pred.stops}


def test_moving_two_minutes_late():
    p = by_id(BaselinePredictor().predict(NOW, STOPS, LiveIn("moving", 0, 2, "ST-ROSEBANK")))
    assert p["ST-RONDEBOSCH"].predicted_min == 7 * 60 + 20       # 07:20
    assert p["ST-CLAREMONT"].predicted_min == 7 * 60 + 27        # 07:27
    assert p["ST-MOWBRAY"].passed


def test_stopped_pushes_eta_out_and_widens_range():
    moving = by_id(BaselinePredictor().predict(NOW, STOPS, LiveIn("moving", 0, 2, "ST-ROSEBANK")))
    stopped = by_id(BaselinePredictor().predict(NOW, STOPS, LiveIn("stopped", 437, 2, "ST-ROSEBANK")))
    r_m, r_s = moving["ST-RONDEBOSCH"], stopped["ST-RONDEBOSCH"]
    assert r_s.predicted_min >= r_m.predicted_min + 9
    assert (r_s.high_min - r_s.low_min) > (r_m.high_min - r_m.low_min)


def test_no_live_data_uses_timetable():
    p = by_id(BaselinePredictor().predict(NOW, STOPS, None))
    assert p["ST-RONDEBOSCH"].predicted_min == 7 * 60 + 18


class FakeNetwork:
    async def get(self, path, **_):
        return {"train_no": "3412", "stops": [
            {"station_id": s.station_id, "station_name": s.station_name,
             "scheduled": f"{s.scheduled_min // 60:02d}:{s.scheduled_min % 60:02d}"} for s in STOPS]}


class FakeTracking:
    def __init__(self, live=True):
        self.live = live

    async def get(self, path, **_):
        if not self.live:
            raise NotFound("no live data", code="no_live_data")
        return {"state": "moving", "stationary_s": 0, "delay_min": 2,
                "next_station_id": "ST-ROSEBANK"}


async def test_journey_endpoint_for_trip_card():
    app = build_app(Settings(environment="test"), network=FakeNetwork(), tracking=FakeTracking())
    async with client_for(app) as c:
        r = await c.get("/trains/3412/journey",
                        params={"board": "ST-RONDEBOSCH", "alight": "ST-CLAREMONT"})
        assert r.status_code == 200, r.text
        j = r.json()
        assert j["board_scheduled"] == "07:18"
        assert j["board_predicted"] == "07:20"
        assert j["alight_predicted"] == "07:27"
        assert j["ride_min"] == 7
        assert j["stops_until_board"] == 2  # "2 stops until Rondebosch": Rosebank, Rondebosch


async def test_eta_works_without_live_data():
    app = build_app(Settings(environment="test"), network=FakeNetwork(),
                    tracking=FakeTracking(live=False))
    async with client_for(app) as c:
        j = (await c.get("/trains/3412/eta")).json()
        assert j["live"] is False and j["stops"][2]["predicted"] == "07:18"
