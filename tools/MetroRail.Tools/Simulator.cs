using System.Net.Http.Json;
using System.Text.Json;

namespace MetroRail.Tools;

/// <summary>
/// Demo train simulator: moves train 3412 down the Southern line, or forces held / stopped.
/// Uses a dev "feed" token, so it only works when services run with ENVIRONMENT=local.
/// Pair it with DEMO_TIME=07:14 for the pitch.
/// </summary>
internal static class Simulator
{
    public static async Task<int> RunAsync(string[] args)
    {
        var api = Args.Get(args, "--api", "http://localhost:8080/api").TrimEnd('/') + "/";
        var train = Args.Get(args, "--train", "3412");
        var mode = Args.Get(args, "--mode", "moving");
        var speed = double.Parse(Args.Get(args, "--speed", "1"));
        var late = int.Parse(Args.Get(args, "--late", "2"));
        var every = double.Parse(Args.Get(args, "--every", "5"));

        using var c = new HttpClient { BaseAddress = new Uri(api) };
        c.DefaultRequestHeaders.Add("Authorization", "Bearer dev:simulator:feed");
        var trip = await c.GetFromJsonAsync<JsonElement>($"network/trips/{train}");
        var coords = (await c.GetFromJsonAsync<JsonElement>("network/stations")).EnumerateArray()
            .ToDictionary(s => s.GetProperty("id").GetString()!, s => (Lat: s.GetProperty("lat").GetDouble(), Lng: s.GetProperty("lng").GetDouble()));
        var stops = trip.GetProperty("stops").EnumerateArray().Select(s => (
            Name: s.GetProperty("station_name").GetString()!, Pos: coords[s.GetProperty("station_id").GetString()!],
            Min: Common.Clock.ParseHhmm(s.GetProperty("scheduled").GetString()!) + late)).ToList();

        var i = Math.Max(stops.FindIndex(s => s.Name == "Mowbray"), 0); // start between Mowbray and Rosebank
        var t = 0.5;
        Console.WriteLine($"Simulating train {train} ({mode}) from {stops[i].Name} -> {stops[i + 1].Name}");
        var started = DateTime.UtcNow;
        while (i < stops.Count - 1)
        {
            (double Lat, double Lng) p;
            if (mode == "held") p = stops[i + 1].Pos;
            else if (mode == "stopped") p = ((stops[i].Pos.Lat + stops[i + 1].Pos.Lat) / 2, (stops[i].Pos.Lng + stops[i + 1].Pos.Lng) / 2);
            else
            {
                var segS = Math.Max((stops[i + 1].Min - stops[i].Min) * 60, 60) / speed;
                t += every / segS;
                if (t >= 1) { i++; t = 0; if (i >= stops.Count - 1) break; }
                p = (stops[i].Pos.Lat + (stops[i + 1].Pos.Lat - stops[i].Pos.Lat) * t, stops[i].Pos.Lng + (stops[i + 1].Pos.Lng - stops[i].Pos.Lng) * t);
            }
            var r = await c.PostAsJsonAsync("tracking/positions", new { train_no = train, lat = p.Lat, lng = p.Lng, source = "simulator" });
            var b = await r.Content.ReadFromJsonAsync<JsonElement>();
            Console.WriteLine(r.IsSuccessStatusCode
                ? $"[{(DateTime.UtcNow - started).TotalSeconds,6:0}s] {b.GetProperty("state").GetString(),10}  {b.GetProperty("last_station_id")} -> {b.GetProperty("next_station_id")}  still {b.GetProperty("stationary_s")}s"
                : $"error {(int)r.StatusCode}: {b}");
            await Task.Delay(TimeSpan.FromSeconds(every));
        }
        return 0;
    }
}
