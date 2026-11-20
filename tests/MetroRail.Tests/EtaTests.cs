using MetroRail.Common;
using MetroRail.Eta;
using Microsoft.Extensions.DependencyInjection;

namespace MetroRail.Tests;

public class EtaTests
{
    private static readonly StopIn[] Stops =
    [
        new("ST-MOWBRAY", "Mowbray", 433), new("ST-ROSEBANK", "Rosebank", 435), new("ST-RONDEBOSCH", "Rondebosch", 438),
        new("ST-NEWLANDS", "Newlands", 441), new("ST-CLAREMONT", "Claremont", 445),
    ];
    private const int Now = 7 * 60 + 14;

    private static Dictionary<string, StopEta> ById(Prediction p) => p.Stops.ToDictionary(s => s.StationId);

    [Fact]
    public void Moving_two_minutes_late()
    {
        var p = ById(new BaselinePredictor().Predict(Now, Stops, new LiveIn("moving", 0, 2, "ST-ROSEBANK")));
        Assert.Equal(7 * 60 + 20, p["ST-RONDEBOSCH"].PredictedMin); // 07:20
        Assert.Equal(7 * 60 + 27, p["ST-CLAREMONT"].PredictedMin);  // 07:27
        Assert.True(p["ST-MOWBRAY"].Passed);
    }

    [Fact]
    public void Stopped_pushes_eta_out_and_widens_range()
    {
        var moving = ById(new BaselinePredictor().Predict(Now, Stops, new LiveIn("moving", 0, 2, "ST-ROSEBANK")))["ST-RONDEBOSCH"];
        var stopped = ById(new BaselinePredictor().Predict(Now, Stops, new LiveIn("stopped", 437, 2, "ST-ROSEBANK")))["ST-RONDEBOSCH"];
        Assert.True(stopped.PredictedMin >= moving.PredictedMin + 9);
        Assert.True(stopped.HighMin - stopped.LowMin > moving.HighMin - moving.LowMin);
    }

    [Fact]
    public void No_live_data_uses_timetable() =>
        Assert.Equal(438, ById(new BaselinePredictor().Predict(Now, Stops, null))["ST-RONDEBOSCH"].PredictedMin);

    private static Task<TestApp> Start(bool live) => TestApp.StartAsync(EtaService.CreateBuilder, EtaService.Build, "eta", b =>
    {
        b.Services.AddKeyedSingleton<IServiceClient>("network", new FakeService(_ => new
        {
            train_no = "3412",
            stops = Stops.Select(s => new { station_id = s.StationId, station_name = s.StationName, scheduled = Clock.Hhmm(s.ScheduledMin) }),
        }));
        b.Services.AddKeyedSingleton<IServiceClient>("tracking", new FakeService(_ => live
            ? new { state = "moving", stationary_s = 0, delay_min = 2, next_station_id = "ST-ROSEBANK" } : null));
        b.Services.AddSingleton<IClock>(new FixedClock(new DateTimeOffset(2026, 11, 20, 5, 14, 0, TimeSpan.Zero)));
    });

    [Fact]
    public async Task Journey_endpoint_for_trip_card()
    {
        await using var t = await Start(live: true);
        var (status, j) = await t.Client.Get("/trains/3412/journey?board=ST-RONDEBOSCH&alight=ST-CLAREMONT");
        Assert.Equal(200, status);
        Assert.Equal("07:18", j.Str("board_scheduled"));
        Assert.Equal("07:20", j.Str("board_predicted"));
        Assert.Equal("07:27", j.Str("alight_predicted"));
        Assert.Equal(7, j.GetProperty("ride_min").GetInt32());
        Assert.Equal(2, j.GetProperty("stops_until_board").GetInt32());
        Assert.Equal(6, j.GetProperty("board_in_min").GetInt32());
    }

    [Fact]
    public async Task Eta_works_without_live_data()
    {
        await using var t = await Start(live: false);
        var (_, j) = await t.Client.Get("/trains/3412/eta");
        Assert.False(j.GetProperty("live").GetBoolean());
        Assert.Equal("07:18", j.GetProperty("stops")[2].Str("predicted"));
    }
}
