from fastapi import APIRouter, Depends, Query
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from metrorail_common.app import get_session
from metrorail_common.errors import AppError, NotFound
from metrorail_common.timeutil import SAST, now_utc

from .logic import Stop, direction_between, fmt, ordered_stops
from .models import Line, LineStop, ScheduledTrip, Station
from .schemas import (
    DepartureOut,
    JourneyOption,
    LineOut,
    LineStopOut,
    StationOut,
    StopTime,
    TripOut,
)

router = APIRouter(tags=["network"])


def _now_min() -> int:
    n = now_utc().astimezone(SAST)
    return n.hour * 60 + n.minute


async def _line_stops(s: AsyncSession, line_id: str) -> list[Stop]:
    rows = (await s.execute(
        select(LineStop, Station).join(Station, Station.id == LineStop.station_id)
        .where(LineStop.line_id == line_id).order_by(LineStop.seq)
    )).all()
    out, acc = [], 0
    for ls, st in rows:
        acc += ls.minutes_from_previous
        out.append(Stop(ls.seq, st.id, st.name, acc))
    return out


@router.get("/stations", response_model=list[StationOut])
async def list_stations(q: str | None = None, s: AsyncSession = Depends(get_session)):
    stmt = select(Station).order_by(Station.name)
    if q:
        stmt = stmt.where(Station.name.ilike(f"%{q}%"))
    stations = (await s.scalars(stmt)).all()
    line_of = {r.station_id: r.line_id for r in (await s.scalars(select(LineStop))).all()}
    return [StationOut(id=x.id, name=x.name, lat=x.lat, lng=x.lng,
                       lines=[line_of[x.id]] if x.id in line_of else []) for x in stations]


@router.get("/stations/nearest", response_model=StationOut)
async def nearest_station(lat: float, lng: float, s: AsyncSession = Depends(get_session)):
    stations = (await s.scalars(select(Station))).all()
    if not stations:
        raise NotFound("No stations loaded")
    best = min(stations, key=lambda x: (x.lat - lat) ** 2 + (x.lng - lng) ** 2)
    return StationOut(id=best.id, name=best.name, lat=best.lat, lng=best.lng)


@router.get("/lines", response_model=list[LineOut])
async def list_lines(s: AsyncSession = Depends(get_session)):
    return [LineOut(id=x.id, name=x.name, colour=x.colour) for x in (await s.scalars(select(Line)))]


@router.get("/lines/{line_id}/stops", response_model=list[LineStopOut])
async def line_stops(line_id: str, s: AsyncSession = Depends(get_session)):
    stops = await _line_stops(s, line_id)
    if not stops:
        raise NotFound(f"Line {line_id} not found", code="line_not_found")
    return [LineStopOut(seq=x.seq, station_id=x.station_id, station_name=x.station_name,
                        minutes_from_origin=x.minutes_from_origin) for x in stops]


async def _trip(s: AsyncSession, train_no: str) -> tuple[ScheduledTrip, list[Stop]]:
    trip = await s.get(ScheduledTrip, train_no)
    if not trip:
        raise NotFound(f"Train {train_no} is not in the timetable", code="train_not_found")
    return trip, ordered_stops(await _line_stops(s, trip.line_id), trip.direction)


@router.get("/trips/{train_no}", response_model=TripOut)
async def get_trip(train_no: str, s: AsyncSession = Depends(get_session)):
    """Timetabled stop times for one train. Tracking/ETA services use this as the baseline."""
    trip, stops = await _trip(s, train_no)
    return TripOut(
        train_no=trip.train_no, line_id=trip.line_id, direction=trip.direction,
        terminus=stops[-1].station_name, platform=trip.platform,
        stops=[StopTime(seq=x.seq, station_id=x.station_id, station_name=x.station_name,
                        scheduled=fmt(trip.departs_min + x.minutes_from_origin)) for x in stops],
    )


@router.get("/stations/{station_id}/departures", response_model=list[DepartureOut])
async def departures(
    station_id: str,
    direction: str | None = Query(None, pattern="^(southbound|northbound)$"),
    after: str | None = Query(None, description="HH:MM SAST, default now"),
    limit: int = Query(6, le=50),
    s: AsyncSession = Depends(get_session),
):
    st = await s.get(Station, station_id)
    if not st:
        raise NotFound(f"Station {station_id} not found", code="station_not_found")
    start = int(after[:2]) * 60 + int(after[3:]) if after else _now_min()
    out: list[DepartureOut] = []
    trips = (await s.scalars(select(ScheduledTrip))).all()
    cache: dict[tuple[str, str], list[Stop]] = {}
    for t in trips:
        if direction and t.direction != direction:
            continue
        key = (t.line_id, t.direction)
        if key not in cache:
            cache[key] = ordered_stops(await _line_stops(s, t.line_id), t.direction)
        stops = cache[key]
        here = next((x for x in stops if x.station_id == station_id), None)
        if not here or here is stops[-1]:
            continue  # train doesn't call here, or terminates here
        at = t.departs_min + here.minutes_from_origin
        if at >= start:
            out.append(DepartureOut(train_no=t.train_no, line_id=t.line_id, direction=t.direction,
                                    terminus=stops[-1].station_name, platform=t.platform,
                                    scheduled=fmt(at), scheduled_min=at, in_min=at - _now_min()))
    out.sort(key=lambda d: d.scheduled_min)
    return out[:limit]


@router.get("/journeys", response_model=list[JourneyOption])
async def journeys(
    from_station: str = Query(..., alias="from"),
    to_station: str = Query(..., alias="to"),
    after: str | None = None,
    limit: int = Query(3, le=10),
    s: AsyncSession = Depends(get_session),
):
    """Direct journeys on one line. Journeys with changes (e.g. via Mutual) come with more lines."""
    lines = (await s.scalars(select(Line))).all()
    start = int(after[:2]) * 60 + int(after[3:]) if after else _now_min()
    options: list[JourneyOption] = []
    for ln in lines:
        stops = await _line_stops(s, ln.id)
        d = direction_between(stops, from_station, to_station)
        if not d:
            continue
        ordered = ordered_stops(stops, d)
        a = next(x for x in ordered if x.station_id == from_station)
        b = next(x for x in ordered if x.station_id == to_station)
        trips = (await s.scalars(select(ScheduledTrip).where(
            ScheduledTrip.line_id == ln.id, ScheduledTrip.direction == d))).all()
        for t in trips:
            dep, arr = t.departs_min + a.minutes_from_origin, t.departs_min + b.minutes_from_origin
            if dep >= start:
                options.append(JourneyOption(
                    train_no=t.train_no, line_id=ln.id, from_station=a.station_name,
                    to_station=b.station_name, departs=fmt(dep), arrives=fmt(arr),
                    duration_min=arr - dep, platform=t.platform))
    if not options and not lines:
        raise AppError("No network data loaded", code="no_network")
    options.sort(key=lambda o: o.departs)
    return options[:limit]
