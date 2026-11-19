from pydantic import BaseModel


class StationOut(BaseModel):
    id: str
    name: str
    lat: float
    lng: float
    lines: list[str] = []


class StopTime(BaseModel):
    seq: int
    station_id: str
    station_name: str
    scheduled: str  # HH:MM SAST


class LineOut(BaseModel):
    id: str
    name: str
    colour: str


class LineStopOut(BaseModel):
    seq: int
    station_id: str
    station_name: str
    minutes_from_origin: int


class TripOut(BaseModel):
    train_no: str
    line_id: str
    direction: str
    terminus: str
    platform: str
    stops: list[StopTime]


class DepartureOut(BaseModel):
    train_no: str
    line_id: str
    direction: str
    terminus: str
    platform: str
    scheduled: str
    scheduled_min: int
    in_min: int  # minutes from now (timetable); live ETA comes from the eta service


class JourneyOption(BaseModel):
    train_no: str
    line_id: str
    from_station: str
    to_station: str
    departs: str
    arrives: str
    duration_min: int
    platform: str
    changes: int = 0
