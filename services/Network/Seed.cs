using System.Text.Json;
using MetroRail.Common;
using Microsoft.EntityFrameworkCore;

namespace MetroRail.Network;

/// <summary>Loads the demo network (Southern line) on first start if the DB is empty.
/// Station order is real; coordinates approximate; times are a sample timetable, not PRASA data.</summary>
public static class Seed
{
    public static JsonElement Load()
    {
        using var s = typeof(Seed).Assembly.GetManifestResourceStream("southern_line.json")!;
        return JsonDocument.Parse(s).RootElement.Clone();
    }

    public static async Task<bool> SeedIfEmptyAsync(NetworkDb db)
    {
        if (await db.Stations.AnyAsync()) return false;
        var data = Load();
        foreach (var st in data.GetProperty("stations").EnumerateArray())
            db.Stations.Add(new Station
            {
                Id = st.GetProperty("id").GetString()!, Name = st.GetProperty("name").GetString()!,
                Lat = st.GetProperty("lat").GetDouble(), Lng = st.GetProperty("lng").GetDouble(),
            });
        var platforms = new Dictionary<string, string>();
        foreach (var ln in data.GetProperty("lines").EnumerateArray())
        {
            var id = ln.GetProperty("id").GetString()!;
            db.Lines.Add(new Line { Id = id, Name = ln.GetProperty("name").GetString()!, Colour = ln.GetProperty("colour").GetString()! });
            foreach (var stop in ln.GetProperty("stops").EnumerateArray())
                db.LineStops.Add(new LineStop
                {
                    LineId = id, Seq = stop.GetProperty("seq").GetInt32(),
                    StationId = stop.GetProperty("station_id").GetString()!,
                    MinutesFromPrevious = stop.GetProperty("minutes_from_previous").GetInt32(),
                });
            foreach (var p in ln.GetProperty("platforms").EnumerateObject()) platforms[p.Name] = p.Value.GetString()!;
        }
        var tt = data.GetProperty("timetable");
        var lineId = tt.GetProperty("line_id").GetString()!;
        foreach (var direction in new[] { "southbound", "northbound" })
        {
            var d = tt.GetProperty(direction);
            int t = Clock.ParseHhmm(d.GetProperty("first").GetString()!), last = Clock.ParseHhmm(d.GetProperty("last").GetString()!);
            int n = d.GetProperty("number_start").GetInt32(), step = d.GetProperty("number_step").GetInt32(), every = d.GetProperty("every_min").GetInt32();
            for (; t <= last; t += every, n += step)
                db.ScheduledTrips.Add(new ScheduledTrip { TrainNo = n.ToString(), LineId = lineId, Direction = direction, DepartsMin = t, Platform = platforms[direction] });
        }
        await db.SaveChangesAsync();
        return true;
    }
}
