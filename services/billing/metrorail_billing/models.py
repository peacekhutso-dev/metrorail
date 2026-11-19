from datetime import datetime

from sqlalchemy import DateTime, Integer, String
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column

from metrorail_common.db import utcnow


class Base(DeclarativeBase):
    pass


class Charge(Base):
    """Amount owed for a purchase (from ticketing.purchase.created). The app never sends amounts."""

    __tablename__ = "charges"
    purchase_id: Mapped[str] = mapped_column(String(40), primary_key=True)
    user_id: Mapped[str] = mapped_column(String(40), index=True)
    amount_cents: Mapped[int] = mapped_column(Integer)
    description: Mapped[str] = mapped_column(String(80))


class Payment(Base):
    __tablename__ = "payments"
    id: Mapped[str] = mapped_column(String(40), primary_key=True)  # PAY-xxxx
    purchase_id: Mapped[str] = mapped_column(String(40), index=True)
    user_id: Mapped[str] = mapped_column(String(40), index=True)
    amount_cents: Mapped[int] = mapped_column(Integer)
    description: Mapped[str] = mapped_column(String(80))
    gateway_ref: Mapped[str] = mapped_column(String(60), index=True)
    status: Mapped[str] = mapped_column(String(14))  # requires_card|requires_3ds|succeeded|declined
    brand: Mapped[str | None] = mapped_column(String(12), nullable=True)
    last4: Mapped[str | None] = mapped_column(String(4), nullable=True)
    decline_reason: Mapped[str | None] = mapped_column(String(40), nullable=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow)


class SavedCard(Base):
    __tablename__ = "saved_cards"
    id: Mapped[str] = mapped_column(String(40), primary_key=True)
    user_id: Mapped[str] = mapped_column(String(40), index=True)
    token: Mapped[str] = mapped_column(String(80))  # Pay@ token, never the card number
    brand: Mapped[str] = mapped_column(String(12))
    last4: Mapped[str] = mapped_column(String(4))
    expiry: Mapped[str] = mapped_column(String(5))
