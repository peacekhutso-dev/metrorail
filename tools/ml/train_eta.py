"""Train the ETA model OFFLINE (data-science tooling, not a running service).

    pip install lightgbm pandas mlflow onnxmltools skl2onnx
    python tools/ml/train_eta.py --data passes.csv --out eta.onnx

passes.csv is an export of the tracking service's station_passes joined with the train state at
the time. Features (in this order, float32): state_code, stationary_s, delay_min, stops_ahead, hour.
state_code: moving=0 at_station=1 held=2 stopped=3 disruption=4. Target: remaining_delay_min.

The model is exported to ONNX and served by the C# eta service (ETA_MODEL_PATH=/models/eta.onnx,
Microsoft.ML.OnnxRuntime). Without a model the eta service uses its baseline predictor.
"""

import argparse


def main() -> None:
    import lightgbm as lgb
    import mlflow
    import mlflow.lightgbm
    import pandas as pd

    ap = argparse.ArgumentParser()
    ap.add_argument("--data", required=True)
    ap.add_argument("--out", default="eta.onnx")
    args = ap.parse_args()

    codes = {"moving": 0, "at_station": 1, "held": 2, "stopped": 3, "disruption": 4}
    df = pd.read_csv(args.data)
    df["state_code"] = df["state"].map(codes)
    features = ["state_code", "stationary_s", "delay_min", "stops_ahead", "hour"]
    X, y = df[features].astype("float32"), df["remaining_delay_min"]

    with mlflow.start_run(run_name="eta-lightgbm"):
        model = lgb.LGBMRegressor(n_estimators=300, learning_rate=0.05, num_leaves=31)
        model.fit(X, y)
        mlflow.log_params(model.get_params())
        mlflow.lightgbm.log_model(model, "model", registered_model_name="eta")

    import onnxmltools
    from onnxmltools.convert.common.data_types import FloatTensorType

    onx = onnxmltools.convert_lightgbm(model, initial_types=[("features", FloatTensorType([None, len(features)]))])
    with open(args.out, "wb") as f:
        f.write(onx.SerializeToString())
    print(f"wrote {args.out}")


if __name__ == "__main__":
    main()
