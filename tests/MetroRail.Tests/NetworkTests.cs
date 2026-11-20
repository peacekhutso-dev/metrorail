using MetroRail.Network;

namespace MetroRail.Tests;

public class NetworkTests
{
    private static Task<TestApp> Start() => TestApp.StartAsync(NetworkService.CreateBuilder, NetworkService.Build, "network");

    [Fact]
    public async Task Health_and_seed()
    {
        await using var t = await Start();
        Assert.Equal("ok", (await t.Client.Get("/health")).Body.Str("status"));
        var (_, stations) = await t.Client.Get("/stations?q=ronde");
        Assert.Equal("ST-RONDEBOSCH", stations[0].Str("id"));
    }

    [Fact]
    public async Task Train_3412_reaches_Rondebosch_at_0718()
    {
        await using var t = await Start();
        var (_, trip) = await t.Client.Get("/trips/3412");
        Assert.Equal("southbound", trip.Str("direction"));
        Assert.Equal("Simon's Town", trip.Str("terminus"));
        var times = trip.GetProperty("stops").EnumerateArray().ToDictionary(s => s.Str("station_name"), s => s.Str("scheduled"));
        Assert.Equal("07:01", times["Cape Town"]);
        Assert.Equal("07:18", times["Rondebosch"]);
        Assert.Equal("07:25", times["Claremont"]);
    }

    [Fact]
    public async Task Departures_by_direction()
    {
        await using var t = await Start();
        var (_, south) = await t.Client.Get("/stations/ST-RONDEBOSCH/departures?direction=southbound&after=07:10&limit=3");
        Assert.Equal(["3412", "3414", "3416"], south.EnumerateArray().Select(d => d.Str("train_no")));
        Assert.All(south.EnumerateArray(), d => Assert.Equal("Simon's Town", d.Str("terminus")));
        var (_, north) = await t.Client.Get("/stations/ST-RONDEBOSCH/departures?direction=northbound&after=07:10");
        Assert.All(north.EnumerateArray(), d => Assert.Equal("Cape Town", d.Str("terminus")));
    }

    [Fact]
    public async Task Journey_Rondebosch_to_Claremont()
    {
        await using var t = await Start();
        var (_, opts) = await t.Client.Get("/journeys?from=ST-RONDEBOSCH&to=ST-CLAREMONT&after=07:10");
        Assert.Equal("3412", opts[0].Str("train_no"));
        Assert.Equal("07:18", opts[0].Str("departs"));
        Assert.Equal(7, opts[0].GetProperty("duration_min").GetInt32());
    }

    [Fact]
    public async Task Unknown_train_is_404_with_error_shape()
    {
        await using var t = await Start();
        var (status, body) = await t.Client.Get("/trips/9999");
        Assert.Equal(404, status);
        Assert.Equal("train_not_found", body.GetProperty("error").Str("code"));
    }
}
