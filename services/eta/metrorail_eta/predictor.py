"""ETA prediction.

The app never shows "ML" to riders, but every time on screen comes from here.

Two predictors share one interface:
- BaselinePredictor: timetable + current delay + expected remaining hold time. Works from day one
  with no history, and is the fallback whenever the model is unavailable.
- LightGBMPredictor: learns the remaining-hold time and per-segment run times from history
  (station_passes in tracking). Loaded from MLflow when ETA_MODEL_URI is set. See training/.

Output for each remaining stop: predicted minute-of-day plus a likely range (low, high).
"""

from dataclasses import dataclass, field
from typing import Protocol


@dataclass(frozen=True)
class StopIn:
    station_id: str
    station_name: str
    scheduled_min: int  # minute of day (SAST)


@dataclass(frozen=True)
class LiveIn:
    state: str               # moving | at_station | held | stopped | disruption
    stationary_s: int
    delay_min: int           # delay measured at the last station passed
    next_station_id: str | None


@dataclass
class StopEta:
    station_id: str
    station_name: str
    scheduled_min: int
    predicted_min: int
    low_min: int
    high_min: int
    passed: bool = False


@dataclass
class Prediction:
    stops: list[StopEta] = field(default_factory=list)
    model: str = "baseline"


class Predictor(Protocol):
    name: str

    def predict(self, now_min: int, stops: list[StopIn], live: LiveIn | None) -> Prediction: ...


# Typical extra wait once a train is already stuck (minutes). The model replaces these with
# learned, location-specific values ("Similar stops here took 8-15 min").
HOLD_REMAINING = {"held": 4, "stopped": 11, "disruption": 20}
RANGE = {"moving": (1, 1), "at_station": (1, 1), "held": (1, 2), "stopped": (3, 5),
         "disruption": (5, 15), None: (2, 4)}


class BaselinePredictor:
    name = "baseline"

    def predict(self, now_min: int, stops: list[StopIn], live: LiveIn | None) -> Prediction:
        if live is None:  # no live data: timetable only, wider range
            return Prediction([StopEta(s.station_id, s.station_name, s.scheduled_min,
                                       s.scheduled_min, s.scheduled_min - 2, s.scheduled_min + 4)
                               for s in stops], self.name)

        ids = [s.station_id for s in stops]
        nxt = ids.index(live.next_station_id) if live.next_station_id in ids else len(stops)
        stuck_min = live.stationary_s / 60
        extra = 0.0
        if live.state in HOLD_REMAINING:
            # expected total hold minus what has already passed, at least 1 more minute
            extra = max(HOLD_REMAINING[live.state] - stuck_min, 1.0) + stuck_min
        delay = max(live.delay_min, 0) + extra
        lo, hi = RANGE.get(live.state, RANGE[None])

        out = []
        for i, s in enumerate(stops):
            if i < nxt:
                out.append(StopEta(s.station_id, s.station_name, s.scheduled_min,
                                   s.scheduled_min + live.delay_min,
                                   s.scheduled_min + live.delay_min,
                                   s.scheduled_min + live.delay_min, passed=True))
                continue
            p = max(round(s.scheduled_min + delay), now_min)
            out.append(StopEta(s.station_id, s.station_name, s.scheduled_min, p, p - lo, p + hi))
        return Prediction(out, self.name)


def load_predictor(model_uri: str | None) -> Predictor:
    if not model_uri:
        return BaselinePredictor()
    try:
        from .ml import LightGBMPredictor  # optional dependency group "ml"

        return LightGBMPredictor(model_uri, fallback=BaselinePredictor())
    except Exception:  # noqa: BLE001 - never take ETAs down because the model failed to load
        return BaselinePredictor()
