from datetime import datetime

from sqlalchemy import JSON, Boolean, DateTime, Integer, String
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column

from metrorail_common.db import utcnow


class Base(DeclarativeBase):
    pass


class SavedTrip(Base):
    __tablename__ = "saved_trips"
    id: Mapped[str] = mapped_column(String(40), primary_key=True)  # TRP-xxxx
    user_id: Mapped[str] = mapped_column(String(40), index=True)
    name: Mapped[str] = mapped_column(String(30))
    from_station_id: Mapped[str] = mapped_column(String(40))
    to_station_id: Mapped[str] = mapped_column(String(40))
    days: Mapped[list] = mapped_column(JSON)  # ["Mon","Tue",...]
    usual_time: Mapped[str] = mapped_column(String(5))  # departure from from_station, HH:MM
    train_no: Mapped[str | None] = mapped_column(String(10), nullable=True)
    alert_delays: Mapped[bool] = mapped_column(Boolean, default=True)
    leave_reminder: Mapped[bool] = mapped_column(Boolean, default=True)
    leave_minutes: Mapped[int] = mapped_column(Integer, default=10)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow)
