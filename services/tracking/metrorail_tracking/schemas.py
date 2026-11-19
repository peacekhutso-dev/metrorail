from datetime import datetime
from typing import Literal

from pydantic import BaseModel, Field


class PositionIn(BaseModel):
    train_no: str
    lat: float = Field(ge=-90, le=90)
    lng: float = Field(ge=-180, le=180)
    at: datetime | None = None
    source: Literal["gps", "staff_scan", "crowd", "simulator"] = "gps"


class TrainOut(BaseModel):
    train_no: str
    lat: float
    lng: float
    state: str
    stationary_s: int
    last_station_id: str | None
    next_station_id: str | None
    at_station_id: str | None
    delay_min: int
    seen_at: datetime
    source: str


class ReportIn(BaseModel):
    train_no: str
    station_id: str
    kind: Literal["arrived", "delayed", "crowded", "not_running"]
