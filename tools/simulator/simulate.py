"""Demo train simulator — moves train 3412 down the Southern line and lets you force states.

    uv run python tools/simulator/simulate.py                    # moving, real-time
    uv run python tools/simulator/simulate.py --mode stopped     # stop between Mowbray & Rosebank
    uv run python tools/simulator/simulate.py --speed 4          # 4x faster

Talks to the gateway (default http://localhost:8080/api) with a dev "feed" token, so it only
works when services run with ENVIRONMENT=local. Pair it with DEMO_TIME=07:14 for the pitch.
"""

import argparse
import asyncio
import time

import httpx

FEED = {"Authorization": "Bearer dev:simulator:feed"}


def lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


async def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--api", default="http://localhost:8080/api")
    ap.add_argument("--train", default="3412")
    ap.add_argument("--mode", choices=["moving", "held", "stopped"], default="moving")
    ap.add_argument("--speed", type=float, default=1.0, help="time multiplier for moving")
    ap.add_argument("--late", type=int, default=2, help="minutes late vs timetable")
    ap.add_argument("--every", type=float, default=5.0, help="seconds between GPS fixes")
    a = ap.parse_args()

    async with httpx.AsyncClient(base_url=a.api, timeout=5) as c:
        trip = (await c.get(f"/network/trips/{a.train}")).raise_for_status().json()
        stations = {s["id"]: s for s in (await c.get("/network/stations")).json()}
        stops = [{**x, **stations[x["station_id"]]} for x in trip["stops"]]
        mins = [int(x["scheduled"][:2]) * 60 + int(x["scheduled"][3:]) + a.late for x in stops]
        names = [x["station_name"] for x in stops]

        # Demo starts between Mowbray and Rosebank (2 stops before Rondebosch)
        i = names.index("Mowbray") if "Mowbray" in names else 0
        t = 0.5
        print(f"Simulating train {a.train} ({a.mode}) from {names[i]} -> {names[i + 1]}")
        start = time.monotonic()
        while i < len(stops) - 1:
            if a.mode == "held":
                lat, lng = stops[i + 1]["lat"], stops[i + 1]["lng"]   # stand at next station
            elif a.mode == "stopped":
                lat, lng = lerp(stops[i]["lat"], stops[i + 1]["lat"], 0.5), \
                    lerp(stops[i]["lng"], stops[i + 1]["lng"], 0.5)
            else:
                seg_s = max((mins[i + 1] - mins[i]) * 60, 60) / a.speed
                t += a.every / seg_s
                if t >= 1:
                    i, t = i + 1, 0.0
                    if i >= len(stops) - 1:
                        break
                lat = lerp(stops[i]["lat"], stops[i + 1]["lat"], t)
                lng = lerp(stops[i]["lng"], stops[i + 1]["lng"], t)
            r = await c.post("/tracking/positions", headers=FEED, json={
                "train_no": a.train, "lat": lat, "lng": lng, "source": "simulator"})
            body = r.json()
            print(f"[{time.monotonic() - start:6.0f}s] {body.get('state', r.status_code):>10}  "
                  f"{body.get('last_station_id')} -> {body.get('next_station_id')}  "
                  f"still {body.get('stationary_s')}s")
            await asyncio.sleep(a.every)


if __name__ == "__main__":
    asyncio.run(main())
