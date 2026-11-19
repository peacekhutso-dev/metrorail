from datetime import datetime

from sqlalchemy import Boolean, DateTime, String, UniqueConstraint
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column

from metrorail_common.db import utcnow


class Base(DeclarativeBase):
    pass


class TripWatch(Base):
    """Local copy of saved trips that want alerts (built from trips.* events)."""

    __tablename__ = "trip_watches"
    trip_id: Mapped[str] = mapped_column(String(40), primary_key=True)
    user_id: Mapped[str] = mapped_column(String(40), index=True)
    train_no: Mapped[str] = mapped_column(String(10), index=True)
    name: Mapped[str] = mapped_column(String(30))
    from_station_id: Mapped[str] = mapped_column(String(40))
    usual_time: Mapped[str] = mapped_column(String(5))


class Alert(Base):
    __tablename__ = "alerts"
    __table_args__ = (UniqueConstraint("user_id", "dedupe_key"),)
    id: Mapped[str] = mapped_column(String(40), primary_key=True)
    user_id: Mapped[str] = mapped_column(String(40), index=True)
    kind: Mapped[str] = mapped_column(String(16))  # delay | held | stopped | disruption | leave_now | info
    title: Mapped[str] = mapped_column(String(80))
    body: Mapped[str] = mapped_column(String(300))
    train_no: Mapped[str | None] = mapped_column(String(10), nullable=True)
    dedupe_key: Mapped[str] = mapped_column(String(80))
    read: Mapped[bool] = mapped_column(Boolean, default=False)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow)
