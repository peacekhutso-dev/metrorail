import re
import secrets
import uuid
from datetime import UTC, timedelta
from typing import Literal

from fastapi import APIRouter, Depends, Request
from pydantic import BaseModel, Field, model_validator
from sqlalchemy import delete, select
from sqlalchemy.ext.asyncio import AsyncSession

from metrorail_common.app import get_bus, get_session
from metrorail_common.auth import Principal, current_user
from metrorail_common.errors import AppError, NotFound, Unauthorized
from metrorail_common.timeutil import now_utc

from .models import OtpChallenge, RefreshToken, User
from .tokens import REFRESH_TTL, hash_secret, new_refresh_token

router = APIRouter(tags=["identity"])
CODE_TTL = timedelta(minutes=5)
MAX_ATTEMPTS = 3


def _aware(dt):
    return dt if dt.tzinfo else dt.replace(tzinfo=UTC)


def normalise_phone(raw: str) -> str:
    digits = re.sub(r"\D", "", raw)
    if digits.startswith("0") and len(digits) == 10:
        digits = "27" + digits[1:]
    if not (digits.startswith("27") and len(digits) == 11):
        raise AppError("Enter a South African mobile number", code="invalid_phone")
    return "+" + digits


class StartIn(BaseModel):
    phone: str | None = None
    email: str | None = Field(default=None, pattern=r"^[^@\s]+@[^@\s]+\.[^@\s]+$")

    @model_validator(mode="after")
    def one_of(self):
        if bool(self.phone) == bool(self.email):
            raise ValueError("Send either phone or email")
        return self


class StartOut(BaseModel):
    challenge_id: str
    channel: Literal["sms", "email"]
    sent_to: str
    expires_in_s: int
    dev_code: str | None = None  # only outside production, for the demo


class VerifyIn(BaseModel):
    challenge_id: str
    code: str = Field(pattern=r"^\d{6}$")


class TokensOut(BaseModel):
    access_token: str
    refresh_token: str
    token_type: str = "Bearer"
    expires_in_s: int = 900
    user_id: str
    is_new_user: bool


class Consents(BaseModel):
    trip_alerts: bool = True
    nearest_station: bool = False
    usage_counts: bool = True


class MeOut(BaseModel):
    id: str
    phone_masked: str | None
    email: str | None
    plan: str
    consents: Consents
    member_since: str


def _mask(phone: str | None) -> str | None:
    return f"{phone[:3]} {phone[3:5]} *** {phone[-4:]}" if phone else None


@router.post("/auth/start", response_model=StartOut)
async def start(body: StartIn, request: Request, s: AsyncSession = Depends(get_session)):
    channel = "sms" if body.phone else "email"
    target = normalise_phone(body.phone) if body.phone else body.email.lower()
    code = f"{secrets.randbelow(1_000_000):06d}"
    ch = OtpChallenge(id=uuid.uuid4().hex, target=target, channel=channel,
                      code_hash=hash_secret(code), expires_at=now_utc() + CODE_TTL)
    s.add(ch)
    await s.commit()
    await request.app.state.sender.send(channel, target, code)
    settings = request.app.state.settings
    return StartOut(challenge_id=ch.id, channel=channel,
                    sent_to=_mask(target) if channel == "sms" else target,
                    expires_in_s=int(CODE_TTL.total_seconds()),
                    dev_code=None if settings.is_production else code)


@router.post("/auth/verify", response_model=TokensOut)
async def verify(body: VerifyIn, request: Request, s: AsyncSession = Depends(get_session),
                 bus=Depends(get_bus)):
    ch = await s.get(OtpChallenge, body.challenge_id)
    if not ch or ch.used or _aware(ch.expires_at) < now_utc():
        raise Unauthorized("This code has expired. Ask for a new one.", code="code_expired")
    if ch.attempts >= MAX_ATTEMPTS:
        raise Unauthorized("Too many tries. Ask for a new code.", code="too_many_attempts")
    if not secrets.compare_digest(ch.code_hash, hash_secret(body.code)):
        ch.attempts += 1
        await s.commit()
        left = MAX_ATTEMPTS - ch.attempts
        raise Unauthorized(f"That code didn't match. {left} tries left.", code="code_mismatch")
    ch.used = True
    field = User.phone if ch.channel == "sms" else User.email
    user = await s.scalar(select(User).where(field == ch.target))
    is_new = user is None
    if is_new:
        user = User(id="USR-" + uuid.uuid4().hex[:12],
                    phone=ch.target if ch.channel == "sms" else None,
                    email=ch.target if ch.channel == "email" else None,
                    consents=Consents().model_dump())
        s.add(user)
    refresh, refresh_hash = new_refresh_token()
    s.add(RefreshToken(token_hash=refresh_hash, user_id=user.id,
                       expires_at=now_utc() + REFRESH_TTL))
    await s.commit()
    if is_new:
        await bus.publish("identity.user.created", {"user_id": user.id})
    return TokensOut(access_token=request.app.state.signer.access_token(user.id, user.roles),
                     refresh_token=refresh, user_id=user.id, is_new_user=is_new)


class RefreshIn(BaseModel):
    refresh_token: str


@router.post("/auth/refresh", response_model=TokensOut)
async def refresh(body: RefreshIn, request: Request, s: AsyncSession = Depends(get_session)):
    rt = await s.get(RefreshToken, hash_secret(body.refresh_token))
    if not rt or rt.revoked or _aware(rt.expires_at) < now_utc():
        raise Unauthorized("Please log in again", code="refresh_invalid")
    user = await s.get(User, rt.user_id)
    if not user:
        raise Unauthorized("Please log in again", code="refresh_invalid")
    rt.revoked = True  # rotate
    new, new_hash = new_refresh_token()
    s.add(RefreshToken(token_hash=new_hash, user_id=user.id, expires_at=now_utc() + REFRESH_TTL))
    await s.commit()
    return TokensOut(access_token=request.app.state.signer.access_token(user.id, user.roles),
                     refresh_token=new, user_id=user.id, is_new_user=False)


@router.post("/auth/logout", status_code=204)
async def logout(body: RefreshIn, s: AsyncSession = Depends(get_session)):
    rt = await s.get(RefreshToken, hash_secret(body.refresh_token))
    if rt:
        rt.revoked = True
        await s.commit()


@router.get("/.well-known/jwks.json", tags=["ops"])
async def jwks(request: Request):
    return {"keys": [request.app.state.signer.jwk]}


async def _me(s: AsyncSession, user: Principal) -> User:
    u = await s.get(User, user.user_id)
    if not u:
        raise NotFound("Account not found", code="user_not_found")
    return u


def _me_out(u: User) -> MeOut:
    return MeOut(id=u.id, phone_masked=_mask(u.phone), email=u.email, plan=u.plan,
                 consents=Consents(**(u.consents or {})),
                 member_since=_aware(u.created_at).strftime("%b %Y"))


@router.get("/me", response_model=MeOut)
async def me(user: Principal = Depends(current_user), s: AsyncSession = Depends(get_session)):
    return _me_out(await _me(s, user))


@router.put("/me/consents", response_model=MeOut)
async def set_consents(body: Consents, user: Principal = Depends(current_user),
                       s: AsyncSession = Depends(get_session), bus=Depends(get_bus)):
    u = await _me(s, user)
    u.consents = body.model_dump()
    await s.commit()
    await bus.publish("identity.user.consents_changed", {"user_id": u.id, **u.consents})
    return _me_out(u)


@router.get("/me/export")
async def export_my_data(user: Principal = Depends(current_user),
                         s: AsyncSession = Depends(get_session)):
    """POPIA s23: a copy of the personal information we hold. Other services add their part
    (trips, tickets, receipts) via the gateway's /api/me/export aggregation (TODO)."""
    u = await _me(s, user)
    return {"account": {"id": u.id, "phone": u.phone, "email": u.email, "plan": u.plan,
                        "consents": u.consents, "created_at": u.created_at}}


@router.delete("/me", status_code=204)
async def delete_me(user: Principal = Depends(current_user),
                    s: AsyncSession = Depends(get_session), bus=Depends(get_bus)):
    """POPIA s24: delete the account. Other services purge on identity.user.deleted.
    Payment records are kept only as long as tax law requires, without contact details."""
    u = await _me(s, user)
    await s.execute(delete(RefreshToken).where(RefreshToken.user_id == u.id))
    await s.delete(u)
    await s.commit()
    await bus.publish("identity.user.deleted", {"user_id": u.id})
