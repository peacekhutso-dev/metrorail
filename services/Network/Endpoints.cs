using MetroRail.Common;
using Microsoft.EntityFrameworkCore;

namespace MetroRail.Network;

public static class Endpoints
{
    public static void MapNetwork(this WebApplication app)
    {
        var g = app.MapGroup("").WithTags("network");

        g.MapGet("/stations", async (string? q, NetworkDb db) =>
        {
            var stations = await db.Stations.OrderBy(s => s.Name).ToListAsync();
            if (!string.IsNullOrEmpty(q)) stations = stations.Where(s => s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            var lineOf = (await db.LineStops.ToListAsync()).GroupBy(x => x.StationId).ToDictionary(x => x.Key, x => x.First().LineId);
            return stations.Select(s => new StationOut(s.Id, s.Name, s.Lat, s.Lng, lineOf.TryGetValue(s.Id, out var l) ? [l] : []));
        });

        g.MapGet("/stations/nearest", async (double lat, double lng, NetworkDb db) =>
        {
            var stations = await db.Stations.ToListAsync();
            if (stations.Count == 0) throw new NotFoundException("No stations loaded");
            var best = stations.MinBy(s => Math.Pow(s.Lat - lat, 2) + Math.Pow(s.Lng - lng, 2))!;
            return new StationOut(best.Id, best.Name, best.Lat, best.Lng, []);
        });

        g.MapGet("/lines", async (NetworkDb db) =>
            (await db.Lines.ToListAsync()).Select(l => new LineOut(l.Id, l.Name, l.Colour)));

        g.MapGet("/lines/{lineId}/stops", async (string lineId, NetworkDb db) =>
        {
            var stops = await LineStops(db, lineId);
            if (stops.Count == 0) throw new NotFoundException($"Line {lineId} not found", "line_not_found");
            return stops.Select(s => new LineStopOut(s.Seq, s.StationId, s.StationName, s.MinutesFromOrigin));
        });

        // Timetabled stop times for one train. Tracking and ETA use this as the baseline.
        g.MapGet("/trips/{trainNo}", async (string trainNo, NetworkDb db) =>
        {
            var trip = await db.ScheduledTrips.FindAsync(trainNo)
                       ?? throw new NotFoundException($"Train {trainNo} is not in the timetable", "train_not_found");
            var stops = Timetable.Ordered(await LineStops(db, trip.LineId), trip.Direction);
            return new TripOut(trip.TrainNo, trip.LineId, trip.Direction, stops[^1].StationName, trip.Platform,
                stops.Select(s => new StopTime(s.Seq, s.StationId, s.StationName, Clock.Hhmm(trip.DepartsMin + s.MinutesFromOrigin))).ToList());
        });

        g.MapGet("/stations/{stationId}/departures", async (string stationId, string? direction, string? after, int? limit, NetworkDb db, IClock clock) =>
        {
            if (direction is not null) Check.Matches(direction, "^(southbound|northbound)$", "direction");
            var take = Check.Range(limit ?? 6, 1, 50, "limit");
            if (await db.Stations.FindAsync(stationId) is null)
                throw new NotFoundException($"Station {stationId} not found", "station_not_found");
            var now = Clock.MinuteOfDay(clock.UtcNow);
            var start = after is null ? now : Clock.ParseHhmm(after);
            var cache = new Dictionary<(string, string), List<Stop>>();
            var outList = new List<DepartureOut>();
            foreach (var t in await db.ScheduledTrips.ToListAsync())
            {
                if (direction is not null && t.Direction != direction) continue;
                if (!cache.TryGetValue((t.LineId, t.Direction), out var stops))
                    cache[(t.LineId, t.Direction)] = stops = Timetable.Ordered(await LineStops(db, t.LineId), t.Direction);
                var here = stops.FirstOrDefault(s => s.StationId == stationId);
                if (here is null || here == stops[^1]) continue; // doesn't call here, or terminates here
                var at = t.DepartsMin + here.MinutesFromOrigin;
                if (at >= start)
                    outList.Add(new DepartureOut(t.TrainNo, t.LineId, t.Direction, stops[^1].StationName, t.Platform, Clock.Hhmm(at), at, at - now));
            }
            return outList.OrderBy(d => d.ScheduledMin).Take(take);
        });

        // Direct journeys on one line. Journeys with changes (e.g. via Mutual) come with more lines.
        g.MapGet("/journeys", async (HttpContext ctx, NetworkDb db, IClock clock) =>
        {
            var q = ctx.Request.Query;
            var from = Check.Required(q["from"], "from");
            var to = Check.Required(q["to"], "to");
            var take = Check.Range(int.TryParse(q["limit"], out var l) ? l : 3, 1, 10, "limit");
            var start = q["after"].Count > 0 ? Clock.ParseHhmm(q["after"]!) : Clock.MinuteOfDay(clock.UtcNow);
            var options = new List<JourneyOption>();
            foreach (var ln in await db.Lines.ToListAsync())
            {
                var stops = await LineStops(db, ln.Id);
                var dir = Timetable.DirectionBetween(stops, from, to);
                if (dir is null) continue;
                var ordered = Timetable.Ordered(stops, dir);
                var a = ordered.First(s => s.StationId == from);
                var b = ordered.First(s => s.StationId == to);
                foreach (var t in await db.ScheduledTrips.Where(t => t.LineId == ln.Id && t.Direction == dir).ToListAsync())
                {
                    int dep = t.DepartsMin + a.MinutesFromOrigin, arr = t.DepartsMin + b.MinutesFromOrigin;
                    if (dep >= start)
                        options.Add(new JourneyOption(t.TrainNo, ln.Id, a.StationName, b.StationName, Clock.Hhmm(dep), Clock.Hhmm(arr), arr - dep, t.Platform));
                }
            }
            return options.OrderBy(o => o.Departs, StringComparer.Ordinal).Take(take);
        });
    }

    private static async Task<List<Stop>> LineStops(NetworkDb db, string lineId)
    {
        var rows = await (from ls in db.LineStops
                          join st in db.Stations on ls.StationId equals st.Id
                          where ls.LineId == lineId
                          orderby ls.Seq
                          select new { ls.Seq, st.Id, st.Name, ls.MinutesFromPrevious }).ToListAsync();
        var acc = 0;
        return rows.Select(r => { acc += r.MinutesFromPrevious; return new Stop(r.Seq, r.Id, r.Name, acc); }).ToList();
    }
}
