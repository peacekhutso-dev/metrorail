from datetime import datetime

from sqlalchemy import DateTime, Float, Integer, String
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    pass


class TrainLive(Base):
    """Latest known state of a running train (one row per train)."""

    __tablename__ = "train_live"
    train_no: Mapped[str] = mapped_column(String(10), primary_key=True)
    lat: Mapped[float] = mapped_column(Float)
    lng: Mapped[float] = mapped_column(Float)
    source: Mapped[str] = mapped_column(String(16))  # gps | staff_scan | crowd | simulator
    seen_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    last_moved_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    state: Mapped[str] = mapped_column(String(12), default="moving")
    last_station_id: Mapped[str | None] = mapped_column(String(40), nullable=True)
    next_station_id: Mapped[str | None] = mapped_column(String(40), nullable=True)
    at_station_id: Mapped[str | None] = mapped_column(String(40), nullable=True)
    delay_min: Mapped[int] = mapped_column(Integer, default=0)


class StationPass(Base):
    """Actual arrival at a station — history for delays and for training the ETA model."""

    __tablename__ = "station_passes"
    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    train_no: Mapped[str] = mapped_column(String(10), index=True)
    station_id: Mapped[str] = mapped_column(String(40))
    arrived_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    scheduled: Mapped[str] = mapped_column(String(5))
    delay_min: Mapped[int] = mapped_column(Integer)


class CrowdReport(Base):
    __tablename__ = "crowd_reports"
    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    train_no: Mapped[str] = mapped_column(String(10), index=True)
    station_id: Mapped[str] = mapped_column(String(40))
    kind: Mapped[str] = mapped_column(String(12))  # arrived | delayed | crowded | not_running
    user_id: Mapped[str | None] = mapped_column(String(64), nullable=True)
    at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
