using System.Collections.Concurrent;
using System.Text.Json;
using MetroRail.Common;
using Microsoft.EntityFrameworkCore;

namespace MetroRail.Tracking;

public sealed record TripStop(string StationId, string Scheduled, double Lat, double Lng);

/// <summary>Timetable stops (with coordinates) per train, fetched once from the network service.</summary>
public sealed class TripCache([FromKeyedServices("network")] IServiceClient network)
{
    private readonly ConcurrentDictionary<string, List<TripStop>> _cache = new();

    public async Task<List<TripStop>> StopsAsync(string trainNo)
    {
        if (_cache.TryGetValue(trainNo, out var hit)) return hit;
        var trip = await network.GetAsync($"/trips/{trainNo}");
        var coords = (await network.GetAsync("/stations")).EnumerateArray()
            .ToDictionary(s => s.GetProperty("id").GetString()!, s => (s.GetProperty("lat").GetDouble(), s.GetProperty("lng").GetDouble()));
        var stops = trip.GetProperty("stops").EnumerateArray().Select(s =>
        {
            var id = s.GetProperty("station_id").GetString()!;
            var (lat, lng) = coords[id];
            return new TripStop(id, s.GetProperty("scheduled").GetString()!, lat, lng);
        }).ToList();
        return _cache[trainNo] = stops;
    }
}

public static class Endpoints
{
    private static readonly HashSet<string> Sources = ["gps", "staff_scan", "crowd", "simulator"];
    private static readonly HashSet<string> Kinds = ["arrived", "delayed", "crowded", "not_running"];

    private static TrainOut Out(TrainLive t, DateTimeOffset now) => new(t.TrainNo, t.Lat, t.Lng, t.State,
        (int)(now - t.LastMovedAt).TotalSeconds, t.LastStationId, t.NextStationId, t.AtStationId, t.DelayMin, t.SeenAt, t.Source);

    public static void MapTracking(this WebApplication app)
    {
        var g = app.MapGroup("").WithTags("tracking");

        // Position from a GPS feed, the demo simulator, or a conductor scan (source=staff_scan).
        g.MapPost("/positions", async (PositionIn body, HttpContext ctx, TrackingDb db, TripCache trips, IEventBus bus, IClock clock) =>
        {
            await ctx.RequireRoleAsync("feed");
            Check.That(body.Lat is >= -90 and <= 90 && body.Lng is >= -180 and <= 180, "lat", "Coordinates are out of range");
            var source = body.Source ?? "gps";
            Check.That(Sources.Contains(source), "source", "Unknown source");
            var at = body.At ?? clock.UtcNow;
            var stops = await trips.StopsAsync(body.TrainNo);

            var dists = stops.Select(s => TrainStates.DistanceM(body.Lat, body.Lng, s.Lat, s.Lng)).ToList();
            var nearest = Enumerable.Range(0, stops.Count).MinBy(i => dists[i]);
            var atStation = dists[nearest] <= TrainStates.Default.StationRadiusM;

            var t = await db.Trains.FindAsync(body.TrainNo);
            var prevState = t?.State;
            if (t is null)
            {
                t = new TrainLive { TrainNo = body.TrainNo, Lat = body.Lat, Lng = body.Lng, Source = source, SeenAt = at, LastMovedAt = at };
                db.Trains.Add(t);
            }
            else if (TrainStates.DistanceM(t.Lat, t.Lng, body.Lat, body.Lng) > TrainStates.Default.MovedMetres)
            {
                t.LastMovedAt = at;
            }

            if (atStation)
            {
                var here = stops[nearest];
                t.AtStationId = t.LastStationId = here.StationId;
                t.NextStationId = nearest + 1 < stops.Count ? stops[nearest + 1].StationId : null;
                if (!await db.Passes.AnyAsync(p => p.TrainNo == body.TrainNo && p.StationId == here.StationId))
                {
                    var delay = Clock.MinuteOfDay(at) - Clock.ParseHhmm(here.Scheduled);
                    t.DelayMin = Math.Max(delay, 0);
                    db.Passes.Add(new StationPass { TrainNo = body.TrainNo, StationId = here.StationId, ArrivedAt = at, Scheduled = here.Scheduled, DelayMin = delay });
                }
            }
            else
            {
                t.AtStationId = null;
                // between `nearest` and whichever neighbour is closer to the train
                var before = nearest > 0 && (nearest + 1 >= stops.Count || dists[nearest - 1] < dists[nearest + 1]) ? nearest - 1 : nearest;
                t.LastStationId = stops[before].StationId;
                t.NextStationId = before + 1 < stops.Count ? stops[before + 1].StationId : null;
            }

            (t.Lat, t.Lng, t.SeenAt, t.Source) = (body.Lat, body.Lng, at, source);
            t.State = TrainStates.Classify((at - t.LastMovedAt).TotalSeconds, atStation);
            await db.SaveChangesAsync();

            var o = Out(t, at);
            await bus.PublishAsync("tracking.train.position", o);
            if (t.State != prevState)
            {
                var ev = JsonSerializer.SerializeToNode(o, Json.Options)!.AsObject();
                ev["previous_state"] = prevState;
                await bus.PublishAsync("tracking.train.state_changed", ev);
            }
            return o;
        });

        g.MapGet("/trains/{trainNo}", async (string trainNo, TrackingDb db, IClock clock) =>
        {
            var t = await db.Trains.FindAsync(trainNo) ?? throw new NotFoundException($"No live data for train {trainNo}", "no_live_data");
            return Out(t, clock.UtcNow);
        });

        g.MapGet("/trains", async (TrackingDb db, IClock clock) =>
            (await db.Trains.ToListAsync()).Select(t => Out(t, clock.UtcNow)));

        // "Train arrived / delayed / crowded" from commuters. A weak signal, and labels for the ETA model.
        g.MapPost("/reports", async (ReportIn body, HttpContext ctx, TrackingDb db, IEventBus bus, IClock clock) =>
        {
            Check.That(Kinds.Contains(body.Kind), "kind", "Unknown report kind");
            var user = await ctx.OptionalUserAsync();
            var r = new CrowdReport { TrainNo = body.TrainNo, StationId = body.StationId, Kind = body.Kind, UserId = user?.UserId, At = clock.UtcNow };
            db.Reports.Add(r);
            await db.SaveChangesAsync();
            await bus.PublishAsync("tracking.report.created", new { train_no = r.TrainNo, station_id = r.StationId, kind = r.Kind });
            return Results.Json(new { id = r.Id, status = "thanks" }, Json.Options, statusCode: 201);
        });
    }
}
