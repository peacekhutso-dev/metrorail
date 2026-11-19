"""Pure timetable logic (no I/O) so it is easy to test and reuse."""

from dataclasses import dataclass


@dataclass(frozen=True)
class Stop:
    seq: int
    station_id: str
    station_name: str
    minutes_from_origin: int


def ordered_stops(stops: list[Stop], direction: str) -> list[Stop]:
    """Stops in travel order with minutes from that direction's origin."""
    if direction == "southbound":
        return sorted(stops, key=lambda s: s.seq)
    rev = sorted(stops, key=lambda s: -s.seq)
    total = rev[0].minutes_from_origin
    return [Stop(s.seq, s.station_id, s.station_name, total - s.minutes_from_origin) for s in rev]


def direction_between(stops: list[Stop], from_id: str, to_id: str) -> str | None:
    seq = {s.station_id: s.seq for s in stops}
    if from_id not in seq or to_id not in seq or from_id == to_id:
        return None
    return "southbound" if seq[to_id] > seq[from_id] else "northbound"


def fmt(minutes: int) -> str:
    minutes %= 24 * 60
    return f"{minutes // 60:02d}:{minutes % 60:02d}"
