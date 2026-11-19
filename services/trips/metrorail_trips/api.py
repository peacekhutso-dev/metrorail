import uuid
from typing import Literal

from fastapi import APIRouter, Depends
from pydantic import BaseModel, Field, field_validator
from sqlalchemy import delete, select
from sqlalchemy.ext.asyncio import AsyncSession

from metrorail_common.app import get_bus, get_session
from metrorail_common.auth import Principal, current_user
from metrorail_common.errors import AppError, NotFound

from .models import SavedTrip

router = APIRouter(tags=["trips"])
Day = Literal["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"]
MAX_TRIPS = 10


class TripIn(BaseModel):
    name: str = Field(min_length=1, max_length=30)
    from_station_id: str
    to_station_id: str
    days: list[Day] = Field(min_length=1)
    usual_time: str = Field(pattern=r"^([01]\d|2[0-3]):[0-5]\d$")
    train_no: str | None = None
    alert_delays: bool = True
    leave_reminder: bool = True
    leave_minutes: int = Field(default=10, ge=0, le=60)

    @field_validator("name")
    @classmethod
    def strip(cls, v: str) -> str:
        v = v.strip()
        if not v:
            raise ValueError("Give the trip a name")
        return v


class TripOut(TripIn):
    id: str


def _out(t: SavedTrip) -> TripOut:
    return TripOut(id=t.id, name=t.name, from_station_id=t.from_station_id,
                   to_station_id=t.to_station_id, days=t.days, usual_time=t.usual_time,
                   train_no=t.train_no, alert_delays=t.alert_delays,
                   leave_reminder=t.leave_reminder, leave_minutes=t.leave_minutes)


def _event(t: SavedTrip) -> dict:
    return {**_out(t).model_dump(), "user_id": t.user_id}


async def _own(s: AsyncSession, trip_id: str, user: Principal) -> SavedTrip:
    t = await s.get(SavedTrip, trip_id)
    if not t or t.user_id != user.user_id:
        raise NotFound("Trip not found", code="trip_not_found")
    return t


@router.get("/trips", response_model=list[TripOut])
async def list_trips(user: Principal = Depends(current_user), s: AsyncSession = Depends(get_session)):
    rows = await s.scalars(select(SavedTrip).where(SavedTrip.user_id == user.user_id)
                           .order_by(SavedTrip.usual_time))
    return [_out(t) for t in rows]


@router.post("/trips", response_model=TripOut, status_code=201)
async def add_trip(body: TripIn, user: Principal = Depends(current_user),
                   s: AsyncSession = Depends(get_session), bus=Depends(get_bus)):
    if body.from_station_id == body.to_station_id:
        raise AppError("Pick two different stations", code="same_station")
    count = len((await s.scalars(select(SavedTrip.id).where(SavedTrip.user_id == user.user_id))).all())
    if count >= MAX_TRIPS:
        raise AppError(f"You can save up to {MAX_TRIPS} trips", code="too_many_trips")
    t = SavedTrip(id="TRP-" + uuid.uuid4().hex[:10], user_id=user.user_id, **body.model_dump())
    s.add(t)
    await s.commit()
    await bus.publish("trips.trip.saved", _event(t))
    return _out(t)


@router.put("/trips/{trip_id}", response_model=TripOut)
async def edit_trip(trip_id: str, body: TripIn, user: Principal = Depends(current_user),
                    s: AsyncSession = Depends(get_session), bus=Depends(get_bus)):
    t = await _own(s, trip_id, user)
    for k, v in body.model_dump().items():
        setattr(t, k, v)
    await s.commit()
    await bus.publish("trips.trip.saved", _event(t))
    return _out(t)


@router.delete("/trips/{trip_id}", status_code=204)
async def delete_trip(trip_id: str, user: Principal = Depends(current_user),
                      s: AsyncSession = Depends(get_session), bus=Depends(get_bus)):
    t = await _own(s, trip_id, user)
    await s.delete(t)
    await s.commit()
    await bus.publish("trips.trip.deleted", {"id": trip_id, "user_id": user.user_id})


async def purge_user(app, user_id: str) -> None:
    async with app.state.db.sessionmaker() as s:
        await s.execute(delete(SavedTrip).where(SavedTrip.user_id == user_id))
        await s.commit()
