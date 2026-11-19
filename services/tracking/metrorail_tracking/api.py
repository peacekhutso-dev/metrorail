from datetime import UTC, datetime

from fastapi import APIRouter, Depends, Request
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from metrorail_common.app import get_bus, get_session
from metrorail_common.auth import Principal, optional_user, require_role
from metrorail_common.errors import NotFound
from metrorail_common.timeutil import SAST, now_utc

from .models import CrowdReport, StationPass, TrainLive
from .schemas import PositionIn, ReportIn, TrainOut
from .state import DEFAULT, classify, distance_m

router = APIRouter(tags=["tracking"])


def _minutes(hhmm: str) -> int:
    return int(hhmm[:2]) * 60 + int(hhmm[3:])


async def _trip(request: Request, train_no: str) -> dict:
    cache = request.app.state.trip_cache
    if train_no not in cache:
        trip = await request.app.state.network.get(f"/trips/{train_no}")
        stations = {s["id"]: s for s in await request.app.state.network.get("/stations")}
        for stop in trip["stops"]:
            st = stations[stop["station_id"]]
            stop["lat"], stop["lng"] = st["lat"], st["lng"]
        cache[train_no] = trip
    return cache[train_no]


def _out(t: TrainLive, now: datetime) -> TrainOut:
    return TrainOut(
        train_no=t.train_no, lat=t.lat, lng=t.lng, state=t.state,
        stationary_s=int((now - _aware(t.last_moved_at)).total_seconds()),
        last_station_id=t.last_station_id, next_station_id=t.next_station_id,
        at_station_id=t.at_station_id, delay_min=t.delay_min, seen_at=t.seen_at, source=t.source,
    )


def _aware(dt: datetime) -> datetime:
    # SQLite drops tzinfo; values are always stored in UTC.
    return dt if dt.tzinfo else dt.replace(tzinfo=UTC)


@router.post("/positions", response_model=TrainOut)
async def ingest_position(
    body: PositionIn,
    request: Request,
    s: AsyncSession = Depends(get_session),
    bus=Depends(get_bus),
    _: Principal = Depends(require_role("feed")),
):
    """Position from a GPS feed, the demo simulator, or a conductor scan (source=staff_scan)."""
    at = body.at or now_utc()
    trip = await _trip(request, body.train_no)
    stops = trip["stops"]

    # Where is the train relative to its stops?
    dists = [distance_m(body.lat, body.lng, x["lat"], x["lng"]) for x in stops]
    nearest = min(range(len(stops)), key=lambda i: dists[i])
    at_station = dists[nearest] <= DEFAULT.station_radius_m
    t = await s.get(TrainLive, body.train_no)
    prev_state = t.state if t else None

    if t is None:
        t = TrainLive(train_no=body.train_no, lat=body.lat, lng=body.lng, source=body.source,
                      seen_at=at, last_moved_at=at)
        s.add(t)
    elif distance_m(t.lat, t.lng, body.lat, body.lng) > DEFAULT.moved_metres:
        t.last_moved_at = at

    # Progress: last station passed / next station (stops are in travel order).
    if at_station:
        t.at_station_id = stops[nearest]["station_id"]
        t.last_station_id = stops[nearest]["station_id"]
        t.next_station_id = stops[nearest + 1]["station_id"] if nearest + 1 < len(stops) else None
        if not (await s.scalar(select(StationPass).where(
                StationPass.train_no == body.train_no,
                StationPass.station_id == t.at_station_id))):
            local = at.astimezone(SAST)
            delay = (local.hour * 60 + local.minute) - _minutes(stops[nearest]["scheduled"])
            t.delay_min = max(delay, 0)
            s.add(StationPass(train_no=body.train_no, station_id=t.at_station_id, arrived_at=at,
                              scheduled=stops[nearest]["scheduled"], delay_min=delay))
    else:
        t.at_station_id = None
        # between nearest and its neighbour closest to the train
        before = nearest - 1 if nearest > 0 and (nearest + 1 >= len(stops)
                  or dists[nearest - 1] < dists[nearest + 1]) else nearest
        t.last_station_id = stops[before]["station_id"]
        t.next_station_id = stops[before + 1]["station_id"] if before + 1 < len(stops) else None

    t.lat, t.lng, t.seen_at, t.source = body.lat, body.lng, at, body.source
    stationary = (at - _aware(t.last_moved_at)).total_seconds()
    t.state = classify(stationary, at_station).value
    await s.commit()

    out = _out(t, at)
    await bus.publish("tracking.train.position", out.model_dump(mode="json"))
    if t.state != prev_state:
        await bus.publish("tracking.train.state_changed",
                          {**out.model_dump(mode="json"), "previous_state": prev_state})
    return out


@router.get("/trains/{train_no}", response_model=TrainOut)
async def get_train(train_no: str, s: AsyncSession = Depends(get_session)):
    t = await s.get(TrainLive, train_no)
    if not t:
        raise NotFound(f"No live data for train {train_no}", code="no_live_data")
    return _out(t, now_utc())


@router.get("/trains", response_model=list[TrainOut])
async def list_trains(s: AsyncSession = Depends(get_session)):
    now = now_utc()
    return [_out(t, now) for t in (await s.scalars(select(TrainLive))).all()]


@router.post("/reports", status_code=201)
async def crowd_report(
    body: ReportIn, s: AsyncSession = Depends(get_session), bus=Depends(get_bus),
    user: Principal | None = Depends(optional_user),
):
    """'Train arrived / delayed / crowded' from commuters. Used as a weak signal + for ML labels."""
    r = CrowdReport(train_no=body.train_no, station_id=body.station_id, kind=body.kind,
                    user_id=user.user_id if user else None, at=now_utc())
    s.add(r)
    await s.commit()
    await bus.publish("tracking.report.created", {"train_no": r.train_no,
                      "station_id": r.station_id, "kind": r.kind})
    return {"id": r.id, "status": "thanks"}
