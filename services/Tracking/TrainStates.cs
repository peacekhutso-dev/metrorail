namespace MetroRail.Tracking;

/// <summary>
/// Train state detection — the rules shown to commuters on the trip card:
///   moving      normal
///   at_station  stationary at a platform within normal dwell
///   held        not moving 3+ min at a station, or 2+ min between stations   (amber)
///   stopped     not moving 5+ min                                            (red)
///   disruption  not moving 15+ min -> alert everyone on the route            (red)
/// Pure functions, so thresholds are easy to test and tune.
/// </summary>
public static class TrainStates
{
    public const string Moving = "moving", AtStation = "at_station", Held = "held", Stopped = "stopped", Disruption = "disruption";

    public sealed record Thresholds(
        double MovedMetres = 30, double StationRadiusM = 150, int HeldAtStationS = 180,
        int HeldBetweenStationsS = 120, int StoppedS = 300, int DisruptionS = 900);

    public static readonly Thresholds Default = new();

    public static double DistanceM(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6_371_000;
        double p1 = lat1 * Math.PI / 180, p2 = lat2 * Math.PI / 180;
        double dp = p2 - p1, dl = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Pow(Math.Sin(dp / 2), 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Pow(Math.Sin(dl / 2), 2);
        return 2 * r * Math.Asin(Math.Sqrt(a));
    }

    public static string Classify(double stationaryS, bool atStation, Thresholds? t = null)
    {
        t ??= Default;
        if (stationaryS >= t.DisruptionS) return Disruption;
        if (stationaryS >= t.StoppedS) return Stopped;
        if (stationaryS >= (atStation ? t.HeldAtStationS : t.HeldBetweenStationsS)) return Held;
        if (atStation && stationaryS > 0) return AtStation;
        return Moving;
    }
}
