from datetime import datetime

from sqlalchemy import DateTime, Integer, String, Text, UniqueConstraint
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column

from metrorail_common.db import utcnow


class Base(DeclarativeBase):
    pass


class Purchase(Base):
    __tablename__ = "purchases"
    id: Mapped[str] = mapped_column(String(40), primary_key=True)  # PUR-xxxx
    user_id: Mapped[str] = mapped_column(String(40), index=True)
    kind: Mapped[str] = mapped_column(String(10), default="new")  # new | topup
    product_id: Mapped[str] = mapped_column(String(20))
    from_station_id: Mapped[str] = mapped_column(String(40))
    to_station_id: Mapped[str] = mapped_column(String(40))
    device_public_key: Mapped[str] = mapped_column(Text)
    amount_cents: Mapped[int] = mapped_column(Integer)
    status: Mapped[str] = mapped_column(String(12), default="pending")  # pending|paid|failed
    ticket_id: Mapped[str | None] = mapped_column(String(40), nullable=True)
    topup_trips: Mapped[int] = mapped_column(Integer, default=0)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow)


class Ticket(Base):
    __tablename__ = "tickets"
    id: Mapped[str] = mapped_column(String(40), primary_key=True)  # MR-48213
    user_id: Mapped[str] = mapped_column(String(40), index=True)
    product_id: Mapped[str] = mapped_column(String(20))
    from_station_id: Mapped[str] = mapped_column(String(40))
    to_station_id: Mapped[str] = mapped_column(String(40))
    line_id: Mapped[str] = mapped_column(String(40))
    trips_total: Mapped[int | None] = mapped_column(Integer, nullable=True)
    trips_used: Mapped[int] = mapped_column(Integer, default=0)
    open_trip_until: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    valid_from: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    valid_to: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    scheduled_min: Mapped[int] = mapped_column(Integer, default=7)
    cert: Mapped[str] = mapped_column(Text)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow)


class SeenCode(Base):
    """Nonces already scanned -> a photo/copy of a code can't be used twice."""

    __tablename__ = "seen_codes"
    __table_args__ = (UniqueConstraint("ticket_id", "nonce"),)
    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    ticket_id: Mapped[str] = mapped_column(String(40), index=True)
    nonce: Mapped[str] = mapped_column(String(24))
    slot: Mapped[int] = mapped_column(Integer)


class Validation(Base):
    __tablename__ = "validations"
    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    ticket_id: Mapped[str | None] = mapped_column(String(40), index=True, nullable=True)
    scanner_id: Mapped[str] = mapped_column(String(40))
    train_no: Mapped[str] = mapped_column(String(10))
    result: Mapped[str] = mapped_column(String(10))
    reason: Mapped[str | None] = mapped_column(String(30), nullable=True)
    trip_no: Mapped[int | None] = mapped_column(Integer, nullable=True)
    at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow)
