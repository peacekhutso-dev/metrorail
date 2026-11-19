"""Train state detection — the rules shown to commuters on the trip card.

    MOVING     normal
    AT_STATION stationary at a platform within normal dwell
    HELD       not moving for 3+ min at a station, or 2+ min between stations   (amber)
    STOPPED    not moving for 5+ min                                            (red)
    DISRUPTION not moving for 15+ min -> alert everyone on the route            (red)

Pure functions only, so the thresholds are easy to test and tune.
"""

import math
from dataclasses import dataclass
from enum import StrEnum


class TrainState(StrEnum):
    MOVING = "moving"
    AT_STATION = "at_station"
    HELD = "held"
    STOPPED = "stopped"
    DISRUPTION = "disruption"


@dataclass(frozen=True)
class Thresholds:
    moved_metres: float = 30.0          # GPS jitter below this is "not moving"
    station_radius_m: float = 150.0
    held_at_station_s: int = 180
    held_between_stations_s: int = 120
    stopped_s: int = 300
    disruption_s: int = 900


DEFAULT = Thresholds()


def distance_m(lat1: float, lng1: float, lat2: float, lng2: float) -> float:
    r = 6_371_000
    p1, p2 = math.radians(lat1), math.radians(lat2)
    dp, dl = p2 - p1, math.radians(lng2 - lng1)
    a = math.sin(dp / 2) ** 2 + math.cos(p1) * math.cos(p2) * math.sin(dl / 2) ** 2
    return 2 * r * math.asin(math.sqrt(a))


def classify(stationary_s: float, at_station: bool, t: Thresholds = DEFAULT) -> TrainState:
    if stationary_s >= t.disruption_s:
        return TrainState.DISRUPTION
    if stationary_s >= t.stopped_s:
        return TrainState.STOPPED
    held_after = t.held_at_station_s if at_station else t.held_between_stations_s
    if stationary_s >= held_after:
        return TrainState.HELD
    if at_station and stationary_s > 0:
        return TrainState.AT_STATION
    return TrainState.MOVING


def is_alerting(state: TrainState) -> bool:
    return state in (TrainState.HELD, TrainState.STOPPED, TrainState.DISRUPTION)
