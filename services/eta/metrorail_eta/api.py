from fastapi import APIRouter, Request
from pydantic import BaseModel

from metrorail_common.errors import AppError
from metrorail_common.timeutil import SAST, now_utc

from .predictor import LiveIn, StopIn

router = APIRouter(tags=["eta"])


def _m(hhmm: str) -> int:
    return int(hhmm[:2]) * 60 + int(hhmm[3:])


def _f(m: int) -> str:
    m %= 1440
    return f"{m // 60:02d}:{m % 60:02d}"


class StopEtaOut(BaseModel):
    station_id: str
    station_name: str
    scheduled: str
    predicted: str
    likely_from: str
    likely_to: str
    delay_min: int
    passed: bool


class TrainEtaOut(BaseModel):
    train_no: str
    state: str | None
    stationary_s: int
    live: bool
    model: str
    stops: list[StopEtaOut]


class JourneyEtaOut(BaseModel):
    """Everything the Home trip card needs, in one call."""

    train_no: str
    state: str | None
    stationary_s: int
    board_station: str
    board_scheduled: str
    board_predicted: str
    board_in_min: int
    stops_until_board: int
    alight_station: str
    alight_predicted: str
    ride_min: int
    likely_from: str
    likely_to: str


async def _predict(request: Request, train_no: str) -> TrainEtaOut:
    trip = await request.app.state.network.get(f"/trips/{train_no}")
    live = None
    try:
        t = await request.app.state.tracking.get(f"/trains/{train_no}")
        live = LiveIn(state=t["state"], stationary_s=t["stationary_s"], delay_min=t["delay_min"],
                      next_station_id=t["next_station_id"])
    except AppError:
        pass  # no live data yet -> timetable-based ETA
    now = now_utc().astimezone(SAST)
    stops = [StopIn(x["station_id"], x["station_name"], _m(x["scheduled"])) for x in trip["stops"]]
    pred = request.app.state.predictor.predict(now.hour * 60 + now.minute, stops, live)
    return TrainEtaOut(
        train_no=train_no, state=live.state if live else None,
        stationary_s=live.stationary_s if live else 0, live=live is not None, model=pred.model,
        stops=[StopEtaOut(station_id=s.station_id, station_name=s.station_name,
                          scheduled=_f(s.scheduled_min), predicted=_f(s.predicted_min),
                          likely_from=_f(s.low_min), likely_to=_f(s.high_min),
                          delay_min=s.predicted_min - s.scheduled_min, passed=s.passed)
               for s in pred.stops],
    )


@router.get("/trains/{train_no}/eta", response_model=TrainEtaOut)
async def train_eta(train_no: str, request: Request):
    return await _predict(request, train_no)


@router.get("/trains/{train_no}/journey", response_model=JourneyEtaOut)
async def journey_eta(train_no: str, board: str, alight: str, request: Request):
    """e.g. /trains/3412/journey?board=ST-RONDEBOSCH&alight=ST-CLAREMONT"""
    eta = await _predict(request, train_no)
    ids = [s.station_id for s in eta.stops]
    if board not in ids or alight not in ids or ids.index(board) >= ids.index(alight):
        raise AppError("Train doesn't run between these stations", code="invalid_journey")
    b, a = eta.stops[ids.index(board)], eta.stops[ids.index(alight)]
    now = now_utc().astimezone(SAST)
    upcoming = [s for s in eta.stops if not s.passed]
    return JourneyEtaOut(
        train_no=train_no, state=eta.state, stationary_s=eta.stationary_s,
        board_station=b.station_name, board_scheduled=b.scheduled, board_predicted=b.predicted,
        board_in_min=max(_m(b.predicted) - (now.hour * 60 + now.minute), 0),
        stops_until_board=next((i + 1 for i, s in enumerate(upcoming) if s.station_id == board), 0),
        alight_station=a.station_name, alight_predicted=a.predicted,
        ride_min=_m(a.predicted) - _m(b.predicted),
        likely_from=b.likely_from, likely_to=b.likely_to,
    )
