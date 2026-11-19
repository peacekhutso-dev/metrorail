"""LightGBM predictor (optional, install with `uv sync --extra ml`).

The model predicts *remaining delay in minutes* at each upcoming stop from features:
    state, stationary_s, delay_min, stops_ahead, segment_id, hour, weekday, line_id,
    recent_holds_here (count in last 30 days), median_hold_here_min
Training: services/eta/training/train.py (reads exported station_passes, logs to MLflow).
"""

from .predictor import BaselinePredictor, LiveIn, Prediction, StopIn


class LightGBMPredictor:
    name = "lightgbm"

    def __init__(self, model_uri: str, fallback: BaselinePredictor):
        import mlflow.lightgbm  # noqa: F401 - fail fast if extras missing

        self.model = mlflow.lightgbm.load_model(model_uri)
        self.fallback = fallback

    def predict(self, now_min: int, stops: list[StopIn], live: LiveIn | None) -> Prediction:
        base = self.fallback.predict(now_min, stops, live)
        if live is None:
            return base
        import pandas as pd

        ahead = [s for s in base.stops if not s.passed]
        if not ahead:
            return base
        X = pd.DataFrame([{
            "state": live.state, "stationary_s": live.stationary_s, "delay_min": live.delay_min,
            "stops_ahead": i, "hour": now_min // 60,
        } for i, _ in enumerate(ahead)]).astype({"state": "category"})
        extra = self.model.predict(X)
        for s, d in zip(ahead, extra, strict=True):
            s.predicted_min = max(round(s.scheduled_min + float(d)), now_min)
            s.low_min, s.high_min = s.predicted_min - 1, s.predicted_min + 2
        base.model = self.name
        return base
