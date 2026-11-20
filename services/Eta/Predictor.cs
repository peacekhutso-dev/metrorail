namespace MetroRail.Eta;

/// <summary>
/// ETA prediction. Riders never see "ML", but every time on screen comes from here.
///
/// - <see cref="BaselinePredictor"/>: timetable + current delay + expected remaining hold time.
///   Works from day one with no history; also the fallback whenever the model is unavailable.
/// - <see cref="OnnxPredictor"/>: a LightGBM model trained offline (tools/ml/train_eta.py) and
///   exported to ONNX, run in-process with ONNX Runtime. Enabled by ETA_MODEL_PATH.
/// </summary>
public sealed record StopIn(string StationId, string StationName, int ScheduledMin);
public sealed record LiveIn(string State, int StationaryS, int DelayMin, string? NextStationId);

public sealed class StopEta(string stationId, string stationName, int scheduledMin, int predictedMin, int lowMin, int highMin, bool passed)
{
    public string StationId { get; } = stationId;
    public string StationName { get; } = stationName;
    public int ScheduledMin { get; } = scheduledMin;
    public int PredictedMin { get; set; } = predictedMin;
    public int LowMin { get; set; } = lowMin;
    public int HighMin { get; set; } = highMin;
    public bool Passed { get; } = passed;
}

public sealed record Prediction(List<StopEta> Stops, string Model);

public interface IPredictor
{
    string Name { get; }
    Prediction Predict(int nowMin, IReadOnlyList<StopIn> stops, LiveIn? live);
}

public sealed class BaselinePredictor : IPredictor
{
    // Typical extra wait once a train is already stuck (minutes). The model learns location-specific values.
    private static readonly Dictionary<string, double> HoldRemaining = new() { ["held"] = 4, ["stopped"] = 11, ["disruption"] = 20 };
    private static readonly Dictionary<string, (int Lo, int Hi)> Range = new()
    {
        ["moving"] = (1, 1), ["at_station"] = (1, 1), ["held"] = (1, 2), ["stopped"] = (3, 5), ["disruption"] = (5, 15),
    };

    public string Name => "baseline";

    public Prediction Predict(int nowMin, IReadOnlyList<StopIn> stops, LiveIn? live)
    {
        if (live is null) // no live data: timetable only, wider range
            return new(stops.Select(s => new StopEta(s.StationId, s.StationName, s.ScheduledMin, s.ScheduledMin, s.ScheduledMin - 2, s.ScheduledMin + 4, false)).ToList(), Name);

        var ids = stops.Select(s => s.StationId).ToList();
        var next = live.NextStationId is not null && ids.Contains(live.NextStationId) ? ids.IndexOf(live.NextStationId) : stops.Count;
        var stuckMin = live.StationaryS / 60.0;
        var extra = HoldRemaining.TryGetValue(live.State, out var hold) ? Math.Max(hold - stuckMin, 1.0) + stuckMin : 0;
        var delay = Math.Max(live.DelayMin, 0) + extra;
        var (lo, hi) = Range.GetValueOrDefault(live.State, (2, 4));

        var list = new List<StopEta>();
        for (var i = 0; i < stops.Count; i++)
        {
            var s = stops[i];
            if (i < next)
            {
                var actual = s.ScheduledMin + live.DelayMin;
                list.Add(new StopEta(s.StationId, s.StationName, s.ScheduledMin, actual, actual, actual, true));
                continue;
            }
            var p = Math.Max((int)Math.Round(s.ScheduledMin + delay, MidpointRounding.ToEven), nowMin);
            list.Add(new StopEta(s.StationId, s.StationName, s.ScheduledMin, p, p - lo, p + hi, false));
        }
        return new(list, Name);
    }
}

/// <summary>LightGBM model exported to ONNX. Input features (float32, one row per upcoming stop):
/// state_code, stationary_s, delay_min, stops_ahead, hour. Output: remaining delay in minutes.</summary>
public sealed class OnnxPredictor : IPredictor, IDisposable
{
    private static readonly Dictionary<string, float> StateCode = new() { ["moving"] = 0, ["at_station"] = 1, ["held"] = 2, ["stopped"] = 3, ["disruption"] = 4 };
    private readonly Microsoft.ML.OnnxRuntime.InferenceSession _session;
    private readonly BaselinePredictor _fallback = new();

    public OnnxPredictor(string modelPath) => _session = new Microsoft.ML.OnnxRuntime.InferenceSession(modelPath);

    public string Name => "lightgbm-onnx";

    public Prediction Predict(int nowMin, IReadOnlyList<StopIn> stops, LiveIn? live)
    {
        var baseP = _fallback.Predict(nowMin, stops, live);
        if (live is null) return baseP;
        var ahead = baseP.Stops.Where(s => !s.Passed).ToList();
        if (ahead.Count == 0) return baseP;
        var data = new float[ahead.Count * 5];
        for (var i = 0; i < ahead.Count; i++)
        {
            data[i * 5] = StateCode.GetValueOrDefault(live.State);
            data[i * 5 + 1] = live.StationaryS;
            data[i * 5 + 2] = live.DelayMin;
            data[i * 5 + 3] = i;
            data[i * 5 + 4] = nowMin / 60;
        }
        var input = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(data, [ahead.Count, 5]);
        using var results = _session.Run([Microsoft.ML.OnnxRuntime.NamedOnnxValue.CreateFromTensor(_session.InputMetadata.Keys.First(), input)]);
        var extra = results.First().AsEnumerable<float>().ToArray();
        for (var i = 0; i < ahead.Count; i++)
        {
            var s = ahead[i];
            s.PredictedMin = Math.Max((int)Math.Round(s.ScheduledMin + extra[i]), nowMin);
            (s.LowMin, s.HighMin) = (s.PredictedMin - 1, s.PredictedMin + 2);
        }
        return baseP with { Model = Name };
    }

    public void Dispose() => _session.Dispose();
}

public static class Predictors
{
    /// <summary>Never take ETAs down because the model failed to load.</summary>
    public static IPredictor Load(string? modelPath, ILogger log)
    {
        if (string.IsNullOrEmpty(modelPath)) return new BaselinePredictor();
        try { return new OnnxPredictor(modelPath); }
        catch (Exception e)
        {
            log.LogWarning(e, "eta_model_load_failed; using baseline");
            return new BaselinePredictor();
        }
    }
}
