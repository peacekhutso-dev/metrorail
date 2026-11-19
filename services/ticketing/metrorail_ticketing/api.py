import random
import uuid
from datetime import UTC, datetime, timedelta

from fastapi import APIRouter, Depends, FastAPI, Request
from pydantic import BaseModel, Field
from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import AsyncSession

from metrorail_common.app import get_bus, get_session
from metrorail_common.auth import Principal, current_user, require_role
from metrorail_common.errors import AppError, NotFound
from metrorail_common.timeutil import now_utc

from .crypto import TicketError, issue_cert, read_qr
from .models import Purchase, SeenCode, Ticket, Validation
from .products import PRODUCTS, TOPUPS, journey_window_min, validity

router = APIRouter(tags=["ticketing"])


def _aware(dt: datetime | None) -> datetime | None:
    return dt if dt is None or dt.tzinfo else dt.replace(tzinfo=UTC)


# ---- catalogue -----------------------------------------------------------------------------

class ProductOut(BaseModel):
    id: str
    name: str
    description: str
    price_cents: int
    trips: int | None


@router.get("/products", response_model=list[ProductOut])
async def products():
    return [ProductOut(**p.__dict__) for p in PRODUCTS.values()]


@router.get("/keys")
async def public_keys(request: Request):
    """Public keys for scanners and apps: verify certificates, seal QR codes."""
    return request.app.state.keys.public()


# ---- purchase ------------------------------------------------------------------------------

class PurchaseIn(BaseModel):
    product_id: str
    from_station_id: str
    to_station_id: str
    device_public_key: str = Field(description="P-256 SPKI DER, base64url — from the phone keystore")


class PurchaseOut(BaseModel):
    id: str
    status: str
    amount_cents: int
    description: str
    ticket_id: str | None


def _p_out(p: Purchase) -> PurchaseOut:
    name = PRODUCTS[p.product_id].name if p.kind == "new" else f"Top up {p.topup_trips} trips"
    return PurchaseOut(id=p.id, status=p.status, amount_cents=p.amount_cents,
                       description=name, ticket_id=p.ticket_id)


async def _announce(bus, p: Purchase) -> None:
    # billing keeps the amount from this event, so the app can never choose its own price
    await bus.publish("ticketing.purchase.created", {
        "purchase_id": p.id, "user_id": p.user_id, "amount_cents": p.amount_cents,
        "description": _p_out(p).description})


@router.post("/purchases", response_model=PurchaseOut, status_code=201)
async def create_purchase(body: PurchaseIn, user: Principal = Depends(current_user),
                          s: AsyncSession = Depends(get_session), bus=Depends(get_bus)):
    product = PRODUCTS.get(body.product_id)
    if not product:
        raise NotFound("Unknown ticket type", code="product_not_found")
    if body.from_station_id == body.to_station_id:
        raise AppError("Pick two different stations", code="same_station")
    p = Purchase(id="PUR-" + uuid.uuid4().hex[:10], user_id=user.user_id, product_id=product.id,
                 from_station_id=body.from_station_id, to_station_id=body.to_station_id,
                 device_public_key=body.device_public_key, amount_cents=product.price_cents)
    s.add(p)
    await s.commit()
    await _announce(bus, p)
    return _p_out(p)


class TopUpIn(BaseModel):
    trips: int


@router.post("/tickets/{ticket_id}/topup", response_model=PurchaseOut, status_code=201)
async def top_up(ticket_id: str, body: TopUpIn, user: Principal = Depends(current_user),
                 s: AsyncSession = Depends(get_session), bus=Depends(get_bus)):
    t = await _own_ticket(s, ticket_id, user)
    if t.trips_total is None:
        raise AppError("This pass is already unlimited", code="not_topupable")
    if body.trips not in TOPUPS:
        raise AppError("Top up 10 or 20 trips", code="invalid_topup")
    p = Purchase(id="PUR-" + uuid.uuid4().hex[:10], user_id=user.user_id, kind="topup",
                 product_id=t.product_id, from_station_id=t.from_station_id,
                 to_station_id=t.to_station_id, device_public_key="", ticket_id=t.id,
                 amount_cents=TOPUPS[body.trips], topup_trips=body.trips)
    s.add(p)
    await s.commit()
    await _announce(bus, p)
    return _p_out(p)


@router.get("/purchases/{purchase_id}", response_model=PurchaseOut)
async def get_purchase(purchase_id: str, user: Principal = Depends(current_user),
                       s: AsyncSession = Depends(get_session)):
    p = await s.get(Purchase, purchase_id)
    if not p or p.user_id != user.user_id:
        raise NotFound("Purchase not found", code="purchase_not_found")
    return _p_out(p)


def _cert_payload(t: Ticket, dpk: str) -> dict:
    return {"v": 1, "tid": t.id, "uid": t.user_id, "prd": t.product_id, "from": t.from_station_id,
            "to": t.to_station_id, "line": t.line_id, "trips": t.trips_total,
            "vf": int(_aware(t.valid_from).timestamp()), "vt": int(_aware(t.valid_to).timestamp()),
            "dpk": dpk}


async def on_payment_succeeded(app: FastAPI, ev: dict) -> None:
    """billing.payment.succeeded -> issue the ticket (or add trips)."""
    async with app.state.db.sessionmaker() as s:
        p = await s.get(Purchase, ev["purchase_id"])
        if not p or p.status == "paid":
            return  # unknown or already handled (events can be redelivered)
        p.status = "paid"
        if p.kind == "topup":
            t = await s.get(Ticket, p.ticket_id)
            t.trips_total = (t.trips_total or 0) + p.topup_trips
            payload = _cert_from(t)
            payload["trips"] = t.trips_total
            t.cert = issue_cert(app.state.keys.issuer, payload)
        else:
            vf, vt = validity(p.product_id, now_utc())
            t = Ticket(id=f"MR-{random.randint(10000, 99999)}", user_id=p.user_id,
                       product_id=p.product_id, from_station_id=p.from_station_id,
                       to_station_id=p.to_station_id, line_id="LN-SOUTHERN",
                       trips_total=PRODUCTS[p.product_id].trips, valid_from=vf, valid_to=vt,
                       cert="")
            t.cert = issue_cert(app.state.keys.issuer, _cert_payload(t, p.device_public_key))
            s.add(t)
            p.ticket_id = t.id
        await s.commit()
        await app.state.bus.publish("ticketing.ticket.issued", {
            "ticket_id": t.id, "user_id": t.user_id, "purchase_id": p.id})


def _cert_from(t: Ticket) -> dict:
    import json

    from .crypto import unb64u
    return json.loads(unb64u(t.cert.split(".")[0]))


async def on_payment_failed(app: FastAPI, ev: dict) -> None:
    async with app.state.db.sessionmaker() as s:
        p = await s.get(Purchase, ev["purchase_id"])
        if p and p.status == "pending":
            p.status = "failed"
            await s.commit()


# ---- my tickets ----------------------------------------------------------------------------

class TicketOut(BaseModel):
    id: str
    product_id: str
    product_name: str
    from_station_id: str
    to_station_id: str
    trips_total: int | None
    trips_used: int
    trips_left: int | None
    trip_in_progress: bool
    trip_valid_until: datetime | None
    valid_to: datetime
    status: str  # ready | in_progress | low | used_up | expired
    cert: str


def _t_out(t: Ticket) -> TicketOut:
    now = now_utc()
    open_ = bool(t.open_trip_until and _aware(t.open_trip_until) > now)
    left = None if t.trips_total is None else t.trips_total - t.trips_used
    if _aware(t.valid_to) <= now:
        status = "expired"
    elif open_:
        status = "in_progress"
    elif left == 0:
        status = "used_up"
    elif left is not None and left <= 2 and t.trips_total and t.trips_total > 1:
        status = "low"
    else:
        status = "ready"
    return TicketOut(id=t.id, product_id=t.product_id, product_name=PRODUCTS[t.product_id].name,
                     from_station_id=t.from_station_id, to_station_id=t.to_station_id,
                     trips_total=t.trips_total, trips_used=t.trips_used, trips_left=left,
                     trip_in_progress=open_,
                     trip_valid_until=_aware(t.open_trip_until) if open_ else None,
                     valid_to=_aware(t.valid_to), status=status, cert=t.cert)


async def _own_ticket(s: AsyncSession, ticket_id: str, user: Principal) -> Ticket:
    t = await s.get(Ticket, ticket_id)
    if not t or t.user_id != user.user_id:
        raise NotFound("Ticket not found", code="ticket_not_found")
    return t


@router.get("/tickets", response_model=list[TicketOut])
async def my_tickets(user: Principal = Depends(current_user), s: AsyncSession = Depends(get_session)):
    rows = await s.scalars(select(Ticket).where(Ticket.user_id == user.user_id)
                           .order_by(Ticket.created_at.desc()))
    return [_t_out(t) for t in rows]


@router.get("/tickets/{ticket_id}", response_model=TicketOut)
async def one_ticket(ticket_id: str, user: Principal = Depends(current_user),
                     s: AsyncSession = Depends(get_session)):
    return _t_out(await _own_ticket(s, ticket_id, user))


# ---- validation (conductor scanner) --------------------------------------------------------

class ValidateIn(BaseModel):
    qr: str
    train_no: str
    scanner_id: str = Field(pattern=r"^SC-\d{6}$")
    line_id: str = "LN-SOUTHERN"


class ValidateOut(BaseModel):
    result: str  # valid | rejected
    reason: str | None = None
    ticket_id: str | None = None
    product_name: str | None = None
    route: str | None = None
    trip_no: int | None = None
    trips_left: int | None = None
    valid_until: datetime | None = None
    new_trip_started: bool = False


@router.post("/validations", response_model=ValidateOut)
async def validate(body: ValidateIn, request: Request, s: AsyncSession = Depends(get_session),
                   bus=Depends(get_bus), _: Principal = Depends(require_role("conductor"))):
    """Online validation. The scanner app runs the same checks offline and syncs every 60 s."""
    now = now_utc()

    async def reject(reason: str, ticket_id: str | None = None) -> ValidateOut:
        s.add(Validation(ticket_id=ticket_id, scanner_id=body.scanner_id, train_no=body.train_no,
                         result="rejected", reason=reason))
        await s.commit()
        return ValidateOut(result="rejected", reason=reason, ticket_id=ticket_id)

    try:
        scanned = read_qr(request.app.state.keys, body.qr)  # real clock (anti-replay), not DEMO_TIME
    except TicketError as e:
        return await reject(e.reason)
    tid = scanned.cert["tid"]
    t = await s.get(Ticket, tid)
    if not t:
        return await reject("invalid_ticket", tid)
    if t.cert != _current_cert(t, scanned.cert):
        return await reject("replaced_ticket", tid)  # e.g. old cert from before a top-up

    s.add(SeenCode(ticket_id=tid, nonce=scanned.nonce, slot=scanned.slot))
    try:
        await s.flush()
    except IntegrityError:
        await s.rollback()
        return await reject("already_scanned", tid)

    if not (_aware(t.valid_from) <= now < _aware(t.valid_to)):
        return await reject("outside_validity", tid)
    if t.line_id != body.line_id:
        return await reject("wrong_route", tid)

    new_trip = False
    if not (t.open_trip_until and _aware(t.open_trip_until) > now):
        if t.trips_total is not None and t.trips_used >= t.trips_total:
            return await reject("no_trips_left", tid)
        if t.trips_total is not None:
            t.trips_used += 1
        t.open_trip_until = now + timedelta(minutes=journey_window_min(t.scheduled_min))
        new_trip = True
    s.add(Validation(ticket_id=tid, scanner_id=body.scanner_id, train_no=body.train_no,
                     result="valid", trip_no=t.trips_used))
    await s.commit()
    if new_trip:
        await bus.publish("ticketing.trip.started", {"ticket_id": tid, "train_no": body.train_no,
                                                     "trip_no": t.trips_used})
    left = None if t.trips_total is None else t.trips_total - t.trips_used
    return ValidateOut(result="valid", ticket_id=tid, product_name=PRODUCTS[t.product_id].name,
                       route=f"{t.from_station_id} → {t.to_station_id}", trip_no=t.trips_used,
                       trips_left=left, valid_until=_aware(t.open_trip_until),
                       new_trip_started=new_trip)


def _current_cert(t: Ticket, presented: dict) -> str:
    # The presented cert must be the latest one we issued for this ticket.
    return t.cert if _cert_from(t) == presented else ""
