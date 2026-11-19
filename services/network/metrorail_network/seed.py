"""Loads the demo network (Southern line) on first start if the DB is empty."""

import json
from importlib import resources

from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from .models import Line, LineStop, ScheduledTrip, Station


def _to_min(hhmm: str) -> int:
    h, m = hhmm.split(":")
    return int(h) * 60 + int(m)


def load_seed() -> dict:
    return json.loads(resources.files("metrorail_network").joinpath("data/southern_line.json").read_text())


async def seed_if_empty(s: AsyncSession) -> bool:
    if (await s.scalar(select(func.count()).select_from(Station))) or 0:
        return False
    data = load_seed()
    s.add_all(Station(**st) for st in data["stations"])
    for ln in data["lines"]:
        s.add(Line(id=ln["id"], name=ln["name"], colour=ln["colour"]))
        s.add_all(LineStop(line_id=ln["id"], **stop) for stop in ln["stops"])
    tt = data["timetable"]
    platforms = data["lines"][0]["platforms"]
    for direction in ("southbound", "northbound"):
        d = tt[direction]
        t, n = _to_min(d["first"]), d["number_start"]
        while t <= _to_min(d["last"]):
            s.add(ScheduledTrip(train_no=str(n), line_id=tt["line_id"], direction=direction,
                                departs_min=t, platform=platforms[direction]))
            t += d["every_min"]
            n += d["number_step"]
    await s.commit()
    return True
