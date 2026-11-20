using MetroRail.Common;
using MetroRail.Tracking;
using Microsoft.Extensions.DependencyInjection;

namespace MetroRail.Tests;

public class TrackingTests
{
    // Minimal network: train 3412, Mowbray -> Rosebank -> Rondebosch.
    private static readonly FakeService Network = new(path => path == "/stations"
        ? new object[]
        {
            new { id = "ST-MOWBRAY", lat = -33.9466, lng = 18.4740 },
            new { id = "ST-ROSEBANK", lat = -33.9543, lng = 18.4734 },
            new { id = "ST-RONDEBOSCH", lat = -33.9612, lng = 18.4705 },
        }
        : new
        {
            train_no = "3412",
            stops = new[]
            {
                new { station_id = "ST-MOWBRAY", scheduled = "07:13" },
                new { station_id = "ST-ROSEBANK", scheduled = "07:15" },
                new { station_id = "ST-RONDEBOSCH", scheduled = "07:18" },
            },
        });

    private static Task<TestApp> Start() => TestApp.StartAsync(TrackingService.CreateBuilder, TrackingService.Build, "tracking",
        b => b.Services.AddKeyedSingleton<IServiceClient>("network", Network));

    [Theory]
    [InlineData(60, true, "at_station")]
    [InlineData(130, false, "held")]        // 2+ min between stations
    [InlineData(130, true, "at_station")]
    [InlineData(200, true, "held")]         // 3+ min at a station
    [InlineData(300, false, "stopped")]     // 5+ min
    [InlineData(900, false, "disruption")]  // 15+ min
    public void Thresholds(int seconds, bool atStation, string expected) =>
        Assert.Equal(expected, TrainStates.Classify(seconds, atStation));

    [Fact]
    public async Task Train_goes_held_then_stopped_between_stations()
    {
        await using var t = await Start();
        var t0 = new DateTimeOffset(2026, 11, 20, 5, 14, 0, TimeSpan.Zero); // 07:14 SAST
        object Pos(int s) => new { train_no = "3412", lat = -33.9500, lng = 18.4737, at = t0.AddSeconds(s) }; // Mowbray-Rosebank

        var (status, r) = await t.Client.Post("/positions", Pos(0), "feed-1", "feed");
        Assert.Equal(200, status);
        Assert.Equal("moving", r.Str("state"));
        Assert.Equal("ST-MOWBRAY", r.Str("last_station_id"));
        Assert.Equal("ST-ROSEBANK", r.Str("next_station_id"));
        Assert.Equal("held", (await t.Client.Post("/positions", Pos(150), "feed-1", "feed")).Body.Str("state"));
        Assert.Equal("stopped", (await t.Client.Post("/positions", Pos(310), "feed-1", "feed")).Body.Str("state"));
        Assert.Equal(3, t.Bus.PublishedSubjects.Count(s => s == "tracking.train.state_changed")); // moving, held, stopped
    }

    [Fact]
    public async Task Delay_recorded_when_train_reaches_station()
    {
        await using var t = await Start();
        var at = new DateTimeOffset(2026, 11, 20, 5, 17, 0, TimeSpan.Zero); // 07:17 at Rosebank, scheduled 07:15
        var (_, r) = await t.Client.Post("/positions", new { train_no = "3412", lat = -33.9543, lng = 18.4734, at }, "feed-1", "feed");
        Assert.Equal("ST-ROSEBANK", r.Str("at_station_id"));
        Assert.Equal(2, r.GetProperty("delay_min").GetInt32());
        Assert.Equal("ST-RONDEBOSCH", r.Str("next_station_id"));
    }

    [Fact]
    public async Task Positions_require_feed_role()
    {
        await using var t = await Start();
        var (status, _) = await t.Client.Post("/positions", new { train_no = "3412", lat = 0, lng = 0 }, "someone");
        Assert.Equal(403, status);
    }
}
