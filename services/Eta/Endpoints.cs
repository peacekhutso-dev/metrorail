using MetroRail.Common;

namespace MetroRail.Eta;

public sealed record StopEtaOut(string StationId, string StationName, string Scheduled, string Predicted, string LikelyFrom,
    string LikelyTo, int DelayMin, bool Passed);
public sealed record TrainEtaOut(string TrainNo, string? State, int StationaryS, bool Live, string Model, List<StopEtaOut> Stops);
/// <summary>Everything the Home trip card needs, in one call.</summary>
public sealed record JourneyEtaOut(string TrainNo, string? State, int StationaryS, string BoardStation, string BoardScheduled,
    string BoardPredicted, int BoardInMin, int StopsUntilBoard, string AlightStation, string AlightPredicted, int RideMin,
    string LikelyFrom, string LikelyTo);

public static class Endpoints
{
    private static async Task<TrainEtaOut> PredictAsync(string trainNo, IServiceProvider sp)
    {
        var network = sp.GetRequiredKeyedService<IServiceClient>("network");
        var tracking = sp.GetRequiredKeyedService<IServiceClient>("tracking");
        var clock = sp.GetRequiredService<IClock>();
        var predictor = sp.GetRequiredService<IPredictor>();

        var trip = await network.GetAsync($"/trips/{trainNo}");
        LiveIn? live = null;
        try
        {
            var t = await tracking.GetAsync($"/trains/{trainNo}");
            live = new LiveIn(t.GetProperty("state").GetString()!, t.GetProperty("stationary_s").GetInt32(),
                t.GetProperty("delay_min").GetInt32(), t.GetProperty("next_station_id").GetString());
        }
        catch (AppException) { /* no live data yet -> timetable-based ETA */ }

        var stops = trip.GetProperty("stops").EnumerateArray().Select(x => new StopIn(
            x.GetProperty("station_id").GetString()!, x.GetProperty("station_name").GetString()!,
            Clock.ParseHhmm(x.GetProperty("scheduled").GetString()!))).ToList();
        var pred = predictor.Predict(Clock.MinuteOfDay(clock.UtcNow), stops, live);
        return new TrainEtaOut(trainNo, live?.State, live?.StationaryS ?? 0, live is not null, pred.Model,
            pred.Stops.Select(s => new StopEtaOut(s.StationId, s.StationName, Clock.Hhmm(s.ScheduledMin), Clock.Hhmm(s.PredictedMin),
                Clock.Hhmm(s.LowMin), Clock.Hhmm(s.HighMin), s.PredictedMin - s.ScheduledMin, s.Passed)).ToList());
    }

    public static void MapEta(this WebApplication app)
    {
        var g = app.MapGroup("").WithTags("eta");

        g.MapGet("/trains/{trainNo}/eta", (string trainNo, HttpContext ctx) => PredictAsync(trainNo, ctx.RequestServices));

        // e.g. /trains/3412/journey?board=ST-RONDEBOSCH&alight=ST-CLAREMONT
        g.MapGet("/trains/{trainNo}/journey", async (string trainNo, string board, string alight, HttpContext ctx, IClock clock) =>
        {
            var eta = await PredictAsync(trainNo, ctx.RequestServices);
            var ids = eta.Stops.Select(s => s.StationId).ToList();
            if (!ids.Contains(board) || !ids.Contains(alight) || ids.IndexOf(board) >= ids.IndexOf(alight))
                throw new AppException("Train doesn't run between these stations", "invalid_journey");
            var b = eta.Stops[ids.IndexOf(board)];
            var a = eta.Stops[ids.IndexOf(alight)];
            var upcoming = eta.Stops.Where(s => !s.Passed).ToList();
            var until = upcoming.FindIndex(s => s.StationId == board) + 1; // "2 stops until Rondebosch": Rosebank, Rondebosch
            return new JourneyEtaOut(trainNo, eta.State, eta.StationaryS, b.StationName, b.Scheduled, b.Predicted,
                Math.Max(Clock.ParseHhmm(b.Predicted) - Clock.MinuteOfDay(clock.UtcNow), 0), Math.Max(until, 0),
                a.StationName, a.Predicted, Clock.ParseHhmm(a.Predicted) - Clock.ParseHhmm(b.Predicted), b.LikelyFrom, b.LikelyTo);
        });
    }
}
