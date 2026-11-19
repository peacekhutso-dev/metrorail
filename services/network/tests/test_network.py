from metrorail_common.testing import client_for
from metrorail_network.main import Settings, build_app


def app():
    return build_app(Settings(environment="test"))


async def test_health_and_seed():
    async with client_for(app()) as c:
        assert (await c.get("/health")).json()["status"] == "ok"
        stations = (await c.get("/stations", params={"q": "ronde"})).json()
        assert stations[0]["id"] == "ST-RONDEBOSCH"


async def test_train_3412_reaches_rondebosch_at_0718():
    async with client_for(app()) as c:
        trip = (await c.get("/trips/3412")).json()
        assert trip["direction"] == "southbound" and trip["terminus"] == "Simon's Town"
        times = {x["station_name"]: x["scheduled"] for x in trip["stops"]}
        assert times["Cape Town"] == "07:01"
        assert times["Rondebosch"] == "07:18"
        assert times["Claremont"] == "07:25"


async def test_departures_by_direction():
    async with client_for(app()) as c:
        r = await c.get("/stations/ST-RONDEBOSCH/departures",
                        params={"direction": "southbound", "after": "07:10", "limit": 3})
        deps = r.json()
        assert [d["train_no"] for d in deps] == ["3412", "3414", "3416"]
        assert all(d["terminus"] == "Simon's Town" for d in deps)
        north = (await c.get("/stations/ST-RONDEBOSCH/departures",
                             params={"direction": "northbound", "after": "07:10"})).json()
        assert all(d["terminus"] == "Cape Town" for d in north)


async def test_journey_rondebosch_to_claremont():
    async with client_for(app()) as c:
        opts = (await c.get("/journeys", params={"from": "ST-RONDEBOSCH", "to": "ST-CLAREMONT",
                                                  "after": "07:10"})).json()
        assert opts[0]["train_no"] == "3412"
        assert opts[0]["departs"] == "07:18" and opts[0]["duration_min"] == 7


async def test_unknown_train_is_404_with_error_shape():
    async with client_for(app()) as c:
        r = await c.get("/trips/9999")
        assert r.status_code == 404
        assert r.json()["error"]["code"] == "train_not_found"
