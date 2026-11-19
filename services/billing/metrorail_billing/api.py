import json
import uuid
from typing import Literal

from fastapi import APIRouter, Depends, FastAPI, Header, Request
from pydantic import BaseModel, Field
from sqlalchemy import delete, select
from sqlalchemy.ext.asyncio import AsyncSession

from metrorail_common.app import get_bus, get_session
from metrorail_common.auth import Principal, current_user
from metrorail_common.errors import AppError, NotFound, Unauthorized

from .models import Charge, Payment, SavedCard
from .payat import GatewayResult

router = APIRouter(tags=["billing"])


class PayIn(BaseModel):
    purchase_id: str
    method: Literal["new_card", "saved_card"]
    saved_card_id: str | None = None


class PaymentOut(BaseModel):
    id: str
    purchase_id: str
    status: str
    amount_cents: int
    description: str
    card_form_url: str | None = None
    three_ds_url: str | None = None
    brand: str | None = None
    last4: str | None = None
    decline_reason: str | None = None


def _out(p: Payment, g: GatewayResult | None = None) -> PaymentOut:
    return PaymentOut(id=p.id, purchase_id=p.purchase_id, status=p.status,
                      amount_cents=p.amount_cents, description=p.description,
                      card_form_url=g.card_form_url if g else None,
                      three_ds_url=g.three_ds_url if g else None, brand=p.brand, last4=p.last4,
                      decline_reason=p.decline_reason)


async def _own(s: AsyncSession, payment_id: str, user: Principal) -> Payment:
    p = await s.get(Payment, payment_id)
    if not p or p.user_id != user.user_id:
        raise NotFound("Payment not found", code="payment_not_found")
    return p


async def _finish(s, bus, p: Payment, g: GatewayResult) -> None:
    p.status = g.status
    p.decline_reason = g.decline_reason
    await s.commit()
    if g.status == "succeeded":
        await bus.publish("billing.payment.succeeded", {
            "payment_id": p.id, "purchase_id": p.purchase_id, "user_id": p.user_id,
            "amount_cents": p.amount_cents})
    elif g.status == "declined":
        await bus.publish("billing.payment.failed", {
            "payment_id": p.id, "purchase_id": p.purchase_id, "reason": g.decline_reason})


@router.post("/payments", response_model=PaymentOut, status_code=201)
async def start_payment(body: PayIn, request: Request, user: Principal = Depends(current_user),
                        s: AsyncSession = Depends(get_session)):
    charge = await s.get(Charge, body.purchase_id)
    if not charge or charge.user_id != user.user_id:
        raise NotFound("Nothing to pay for", code="charge_not_found")
    token = None
    card = None
    if body.method == "saved_card":
        card = await s.get(SavedCard, body.saved_card_id or "")
        if not card or card.user_id != user.user_id:
            raise NotFound("Saved card not found", code="card_not_found")
        token = card.token
    g = await request.app.state.gateway.create(charge.purchase_id, charge.amount_cents, token)
    p = Payment(id="PAY-" + uuid.uuid4().hex[:10], purchase_id=charge.purchase_id,
                user_id=user.user_id, amount_cents=charge.amount_cents,
                description=charge.description, gateway_ref=g.gateway_ref, status=g.status,
                brand=card.brand if card else None, last4=card.last4 if card else None)
    s.add(p)
    await s.commit()
    return _out(p, g)


class CardIn(BaseModel):
    """MOCK ONLY. With real Pay@ the hosted form collects the card; this endpoint is disabled."""

    number: str = Field(pattern=r"^[\d ]{15,23}$")
    expiry: str = Field(pattern=r"^(0[1-9]|1[0-2])/\d{2}$")
    cvv: str = Field(pattern=r"^\d{3,4}$")
    name: str
    save: bool = False


@router.post("/payments/{payment_id}/mock-card", response_model=PaymentOut)
async def mock_card(payment_id: str, body: CardIn, request: Request,
                    user: Principal = Depends(current_user), s: AsyncSession = Depends(get_session)):
    if request.app.state.settings.payat_mode != "mock":
        raise AppError("Card details go to Pay@'s secure form", code="use_hosted_form")
    p = await _own(s, payment_id, user)
    g = await request.app.state.gateway.submit_card(p.gateway_ref, body.number, body.expiry,
                                                    body.cvv, body.name, body.save)
    p.status, p.decline_reason = g.status, g.decline_reason
    if g.last4:
        p.brand, p.last4 = g.brand, g.last4
    if g.card_token:
        s.add(SavedCard(id="CRD-" + uuid.uuid4().hex[:8], user_id=user.user_id, token=g.card_token,
                        brand=g.brand, last4=g.last4, expiry=body.expiry))
    await s.commit()
    return _out(p, g)


class OtpIn(BaseModel):
    otp: str = Field(pattern=r"^\d{6}$")


@router.post("/payments/{payment_id}/mock-3ds", response_model=PaymentOut)
async def mock_3ds(payment_id: str, body: OtpIn, request: Request,
                   user: Principal = Depends(current_user), s: AsyncSession = Depends(get_session),
                   bus=Depends(get_bus)):
    if request.app.state.settings.payat_mode != "mock":
        raise AppError("3-D Secure is completed on the bank's page", code="use_bank_page")
    p = await _own(s, payment_id, user)
    g = await request.app.state.gateway.confirm_3ds(p.gateway_ref, body.otp)
    await _finish(s, bus, p, g)
    return _out(p, g)


@router.post("/webhooks/payat", status_code=204, include_in_schema=False)
async def payat_webhook(request: Request, s: AsyncSession = Depends(get_session),
                        bus=Depends(get_bus), x_payat_signature: str = Header("")):
    """Pay@ tells us the final result (succeeded / declined). Idempotent."""
    raw = await request.body()
    if not request.app.state.gateway.verify_webhook(raw, x_payat_signature):
        raise Unauthorized("Bad signature", code="bad_signature")
    ev = json.loads(raw)
    p = await s.scalar(select(Payment).where(Payment.gateway_ref == ev["gateway_ref"]))
    if p and p.status not in ("succeeded", "declined"):
        await _finish(s, bus, p, GatewayResult(ev["gateway_ref"], ev["status"],
                                               decline_reason=ev.get("reason")))


@router.get("/payments/{payment_id}", response_model=PaymentOut)
async def get_payment(payment_id: str, user: Principal = Depends(current_user),
                      s: AsyncSession = Depends(get_session)):
    return _out(await _own(s, payment_id, user))


@router.get("/receipts", response_model=list[PaymentOut])
async def receipts(user: Principal = Depends(current_user), s: AsyncSession = Depends(get_session)):
    rows = await s.scalars(select(Payment).where(Payment.user_id == user.user_id,
                                                 Payment.status == "succeeded")
                           .order_by(Payment.created_at.desc()))
    return [_out(p) for p in rows]


class CardOut(BaseModel):
    id: str
    brand: str
    last4: str
    expiry: str


@router.get("/cards", response_model=list[CardOut])
async def cards(user: Principal = Depends(current_user), s: AsyncSession = Depends(get_session)):
    rows = await s.scalars(select(SavedCard).where(SavedCard.user_id == user.user_id))
    return [CardOut(id=c.id, brand=c.brand, last4=c.last4, expiry=c.expiry) for c in rows]


@router.delete("/cards/{card_id}", status_code=204)
async def remove_card(card_id: str, user: Principal = Depends(current_user),
                      s: AsyncSession = Depends(get_session)):
    await s.execute(delete(SavedCard).where(SavedCard.id == card_id,
                                            SavedCard.user_id == user.user_id))
    await s.commit()


async def on_purchase_created(app: FastAPI, ev: dict) -> None:
    async with app.state.db.sessionmaker() as s:
        if not await s.get(Charge, ev["purchase_id"]):
            s.add(Charge(purchase_id=ev["purchase_id"], user_id=ev["user_id"],
                         amount_cents=ev["amount_cents"], description=ev["description"]))
            await s.commit()


async def on_user_deleted(app: FastAPI, ev: dict) -> None:
    async with app.state.db.sessionmaker() as s:
        await s.execute(delete(SavedCard).where(SavedCard.user_id == ev["user_id"]))
        await s.commit()
