import uuid

from fastapi import APIRouter, Depends, FastAPI
from pydantic import BaseModel
from sqlalchemy import delete, select, update
from sqlalchemy.ext.asyncio import AsyncSession

from metrorail_common.app import get_session
from metrorail_common.auth import Principal, current_user
from metrorail_common.errors import NotFound
from metrorail_common.timeutil import SAST, now_utc

from .models import Alert, TripWatch
from .rules import for_train_event

router = APIRouter(tags=["notifications"])


class AlertOut(BaseModel):
    id: str
    kind: str
    title: str
    body: str
    train_no: str | None
    read: bool
    created_at: str


@router.get("/alerts", response_model=list[AlertOut])
async def my_alerts(user: Principal = Depends(current_user), s: AsyncSession = Depends(get_session)):
    rows = await s.scalars(select(Alert).where(Alert.user_id == user.user_id)
                           .order_by(Alert.created_at.desc()).limit(50))
    return [AlertOut(id=a.id, kind=a.kind, title=a.title, body=a.body, train_no=a.train_no,
                     read=a.read, created_at=a.created_at.isoformat()) for a in rows]


@router.get("/alerts/unread-count")
async def unread(user: Principal = Depends(current_user), s: AsyncSession = Depends(get_session)):
    rows = await s.scalars(select(Alert.id).where(Alert.user_id == user.user_id, ~Alert.read))
    return {"unread": len(rows.all())}


@router.post("/alerts/{alert_id}/read", status_code=204)
async def mark_read(alert_id: str, user: Principal = Depends(current_user),
                    s: AsyncSession = Depends(get_session)):
    a = await s.get(Alert, alert_id)
    if not a or a.user_id != user.user_id:
        raise NotFound("Alert not found", code="alert_not_found")
    a.read = True
    await s.commit()


@router.post("/alerts/read-all", status_code=204)
async def read_all(user: Principal = Depends(current_user), s: AsyncSession = Depends(get_session)):
    await s.execute(update(Alert).where(Alert.user_id == user.user_id).values(read=True))
    await s.commit()


# ---- event handlers -------------------------------------------------------------------------

async def on_trip_saved(app: FastAPI, ev: dict) -> None:
    async with app.state.db.sessionmaker() as s:
        await s.execute(delete(TripWatch).where(TripWatch.trip_id == ev["id"]))
        if ev.get("alert_delays") and ev.get("train_no"):
            s.add(TripWatch(trip_id=ev["id"], user_id=ev["user_id"], train_no=ev["train_no"],
                            name=ev["name"], from_station_id=ev["from_station_id"],
                            usual_time=ev["usual_time"]))
        await s.commit()


async def on_trip_deleted(app: FastAPI, ev: dict) -> None:
    async with app.state.db.sessionmaker() as s:
        await s.execute(delete(TripWatch).where(TripWatch.trip_id == ev["id"]))
        await s.commit()


async def on_user_deleted(app: FastAPI, ev: dict) -> None:
    async with app.state.db.sessionmaker() as s:
        await s.execute(delete(TripWatch).where(TripWatch.user_id == ev["user_id"]))
        await s.execute(delete(Alert).where(Alert.user_id == ev["user_id"]))
        await s.commit()


async def on_train_event(app: FastAPI, ev: dict) -> None:
    today = now_utc().astimezone(SAST).date().isoformat()
    created = []
    async with app.state.db.sessionmaker() as s:
        watches = (await s.scalars(select(TripWatch).where(TripWatch.train_no == ev["train_no"]))).all()
        for w in watches:
            draft = for_train_event(ev, w.name, w.from_station_id, w.usual_time)
            if not draft:
                continue
            key = f"{today}:{ev['train_no']}:{draft.kind}"
            exists = await s.scalar(select(Alert.id).where(Alert.user_id == w.user_id,
                                                           Alert.dedupe_key == key))
            if exists:
                continue
            a = Alert(id="ALR-" + uuid.uuid4().hex[:10], user_id=w.user_id, kind=draft.kind,
                      title=draft.title, body=draft.body, train_no=ev["train_no"], dedupe_key=key)
            s.add(a)
            created.append(a)
        await s.commit()
    for a in created:  # realtime pushes this to the user's in-app channel
        await app.state.bus.publish("notifications.alert.created", {
            "user_id": a.user_id, "id": a.id, "kind": a.kind, "title": a.title, "body": a.body,
            "train_no": a.train_no})
