"""Train the ETA model.

    uv run --extra ml python services/eta/training/train.py --data passes.csv

passes.csv is an export of tracking.station_passes joined with the train state at the time
(see docs/ml.md). Logs the model to MLflow; set ETA_MODEL_URI=models:/eta/Production to use it.
"""

import argparse


def main() -> None:
    import lightgbm as lgb
    import mlflow
    import mlflow.lightgbm
    import pandas as pd

    ap = argparse.ArgumentParser()
    ap.add_argument("--data", required=True)
    args = ap.parse_args()

    df = pd.read_csv(args.data)
    features = ["state", "stationary_s", "delay_min", "stops_ahead", "hour"]
    df["state"] = df["state"].astype("category")
    X, y = df[features], df["remaining_delay_min"]

    with mlflow.start_run(run_name="eta-lightgbm"):
        model = lgb.LGBMRegressor(n_estimators=300, learning_rate=0.05, num_leaves=31)
        model.fit(X, y)
        mlflow.log_params(model.get_params())
        mlflow.lightgbm.log_model(model, "model", registered_model_name="eta")


if __name__ == "__main__":
    main()
