from sqlalchemy import Float, ForeignKey, Integer, String
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    pass


class Station(Base):
    __tablename__ = "stations"
    id: Mapped[str] = mapped_column(String(40), primary_key=True)  # e.g. ST-RONDEBOSCH
    name: Mapped[str] = mapped_column(String(80), index=True)
    lat: Mapped[float] = mapped_column(Float)
    lng: Mapped[float] = mapped_column(Float)


class Line(Base):
    __tablename__ = "lines"
    id: Mapped[str] = mapped_column(String(40), primary_key=True)  # e.g. LN-SOUTHERN
    name: Mapped[str] = mapped_column(String(80))
    colour: Mapped[str] = mapped_column(String(9))


class LineStop(Base):
    __tablename__ = "line_stops"
    line_id: Mapped[str] = mapped_column(ForeignKey("lines.id"), primary_key=True)
    seq: Mapped[int] = mapped_column(Integer, primary_key=True)
    station_id: Mapped[str] = mapped_column(ForeignKey("stations.id"), index=True)
    minutes_from_previous: Mapped[int] = mapped_column(Integer, default=0)


class ScheduledTrip(Base):
    """One timetabled run of a train (e.g. train 3412, southbound, leaves Cape Town 07:01)."""

    __tablename__ = "scheduled_trips"
    train_no: Mapped[str] = mapped_column(String(10), primary_key=True)
    line_id: Mapped[str] = mapped_column(ForeignKey("lines.id"))
    direction: Mapped[str] = mapped_column(String(12))  # southbound | northbound
    departs_min: Mapped[int] = mapped_column(Integer)  # minutes after midnight (SAST) from origin
    platform: Mapped[str] = mapped_column(String(4))
