using MetroRail.Common;
using MetroRail.Network;
using Microsoft.Extensions.DependencyInjection;

namespace MetroRail.Tests;

/// <summary>
/// Integration tests for the Network microservice using
/// the imported Southern Line weekday timetable.
/// </summary>
public sealed class NetworkTests
{
    // Start an isolated Network service with a fixed clock.
    private static async Task<TestApp> Start(
        DateTimeOffset? utcNow = null)
    {
        var app = await TestApp.StartAsync(
            NetworkService.CreateBuilder,
            NetworkService.Build,
            "network",
            configure: builder =>
            {
                builder.Services.AddSingleton<IClock>(
                    new FixedClock(
                        utcNow ?? new DateTimeOffset(
                            2026, 11, 23, 3, 0, 0,
                            TimeSpan.Zero)));
            });

        try
        {
            // Import the actual timetable into the test database.
            using var scope = app.App.Services.CreateScope();

            var db = scope.ServiceProvider
                .GetRequiredService<NetworkDb>();

            await TimetableImporter.ImportAsync(db);

            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    // =====================================================
    // HEALTH AND NETWORK DATA
    // =====================================================

    [Fact]
    public async Task Health_and_seed()
    {
        await using var t = await Start();

        var (status, health) =
            await t.Client.Get("/health");

        Assert.Equal(200, status);
        Assert.Equal("ok", health.Str("status"));

        var (stationStatus, stations) =
            await t.Client.Get("/stations?q=ronde");

        Assert.Equal(200, stationStatus);
        Assert.Equal(
            "ST-RONDEBOSCH",
            stations[0].Str("id"));
    }

    [Fact]
    public async Task Southern_line_contains_28_stations()
    {
        await using var t = await Start();

        var (status, stations) =
            await t.Client.Get("/stations");

        Assert.Equal(200, status);
        Assert.Equal(28, stations.GetArrayLength());
    }

    // =====================================================
    // IMPORTED TRAIN TIMETABLE
    // =====================================================

    [Fact]
    public async Task Train_0103_has_correct_timetable()
    {
        await using var t = await Start();

        var (status, trip) =
            await t.Client.Get("/trips/0103");

        Assert.Equal(200, status);
        Assert.Equal("southbound", trip.Str("direction"));
        Assert.Equal("Fish Hoek", trip.Str("terminus"));

        // Use original labels because some stations have
        // separate arrival and departure events.
        var times = trip.GetProperty("stops")
            .EnumerateArray()
            .ToDictionary(
                s => s.Str("source_station_label"),
                s => s.Str("scheduled"));

        Assert.Equal("05:00", times["CAPE TOWN"]);
        Assert.Equal("05:15", times["RONDEBOSCH"]);
        Assert.Equal("05:20", times["CLAREMONT"]);
        Assert.Equal("05:38", times["RETREAT (A)"]);
        Assert.Equal("05:39", times["RETREAT (D)"]);
        Assert.Equal("06:03", times["FISH HOEK (A)"]);

        Assert.Equal(26, times.Count);
    }

    [Fact]
    public async Task Train_0100_has_correct_direction()
    {
        await using var t = await Start();

        var (status, trip) =
            await t.Client.Get("/trips/0100");

        Assert.Equal(200, status);
        Assert.Equal("northbound", trip.Str("direction"));

        var stops = trip.GetProperty("stops");

        Assert.Equal(
            "04:25",
            stops[0].Str("scheduled"));

        Assert.Equal(
            "Simon's Town",
            stops[0].Str("station_name"));
    }

    // =====================================================
    // STATION DEPARTURES
    // =====================================================

    [Theory]
    [InlineData("southbound")]
    [InlineData("northbound")]
    public async Task Departures_by_direction(
        string direction)
    {
        await using var t = await Start();

        var (status, departures) = await t.Client.Get(
            "/stations/ST-RONDEBOSCH/departures" +
            $"?direction={direction}&after=05:00&limit=3");

        Assert.Equal(200, status);

        var items = departures.EnumerateArray().ToArray();

        Assert.Equal(3, items.Length);

        Assert.All(items, departure =>
        {
            Assert.Equal(
                direction,
                departure.Str("direction"));
        });

        // Ensure departure times are ordered.
        var times = items.Select(d =>
            d.GetProperty("scheduled_min").GetInt32())
            .ToArray();

        Assert.Equal(
            times.OrderBy(x => x).ToArray(),
            times);
    }

    [Fact]
    public async Task Retreat_departures_use_departure_event()
    {
        await using var t = await Start();

        var (status, departures) = await t.Client.Get(
            "/stations/ST-RETREAT/departures" +
            "?direction=southbound&after=05:00&limit=50");

        Assert.Equal(200, status);

        var items = departures.EnumerateArray().ToArray();

        var train = Assert.Single(
            items,
            d => d.Str("train_no") == "0103");

        Assert.Equal("05:39", train.Str("scheduled"));

        Assert.Equal(
            "RETREAT (D)",
            train.Str("source_station_label"));
    }

    [Fact]
    public async Task Fish_Hoek_excludes_terminating_trains()
    {
        await using var t = await Start();

        var (status, departures) = await t.Client.Get(
            "/stations/ST-FISH-HOEK/departures" +
            "?direction=southbound&after=00:00&limit=50");

        Assert.Equal(200, status);

        var items = departures.EnumerateArray().ToArray();

        Assert.NotEmpty(items);

        Assert.DoesNotContain(
            items,
            d => d.Str("train_no") == "0103");

        Assert.All(items, departure =>
        {
            Assert.NotEqual(
                "Fish Hoek",
                departure.Str("terminus"));
        });
    }

    // =====================================================
    // JOURNEY PLANNING
    // =====================================================

    [Fact]
    public async Task Journey_Rondebosch_to_Claremont()
    {
        await using var t = await Start();

        var (status, options) = await t.Client.Get(
            "/journeys?from=ST-RONDEBOSCH" +
            "&to=ST-CLAREMONT&after=05:00&limit=10");

        Assert.Equal(200, status);

        var train = Assert.Single(
            options.EnumerateArray(),
            o => o.Str("train_no") == "0103");

        Assert.Equal("05:15", train.Str("departs"));
        Assert.Equal("05:20", train.Str("arrives"));

        Assert.Equal(
            5,
            train.GetProperty("duration_min").GetInt32());
    }

    [Fact]
    public async Task Journey_from_Retreat_uses_departure()
    {
        await using var t = await Start();

        var (status, options) = await t.Client.Get(
            "/journeys?from=ST-RETREAT" +
            "&to=ST-FISH-HOEK&after=05:00&limit=10");

        Assert.Equal(200, status);

        var train = Assert.Single(
            options.EnumerateArray(),
            o => o.Str("train_no") == "0103");

        Assert.Equal("05:39", train.Str("departs"));
        Assert.Equal("06:03", train.Str("arrives"));

        Assert.Equal(
            24,
            train.GetProperty("duration_min").GetInt32());
    }

    [Fact]
    public async Task Journey_to_Retreat_uses_arrival()
    {
        await using var t = await Start();

        var (status, options) = await t.Client.Get(
            "/journeys?from=ST-CAPE-TOWN" +
            "&to=ST-RETREAT&after=05:00&limit=10");

        Assert.Equal(200, status);

        var train = Assert.Single(
            options.EnumerateArray(),
            o => o.Str("train_no") == "0103");

        Assert.Equal("05:00", train.Str("departs"));
        Assert.Equal("05:38", train.Str("arrives"));

        Assert.Equal(
            38,
            train.GetProperty("duration_min").GetInt32());
    }

    // =====================================================
    // INVALID REQUESTS
    // =====================================================

    [Theory]
    [InlineData("/journeys?from=ST-RETREAT&to=ST-RETREAT")]
    [InlineData("/journeys?from=ST-RETREAT&to=ST-FISH-HOEK&after=25:00")]
    [InlineData("/journeys?from=ST-RETREAT&to=ST-FISH-HOEK&after=05:75")]
    [InlineData("/journeys?from=ST-RETREAT&to=ST-FISH-HOEK&after=abc")]
    [InlineData("/journeys?from=ST-RETREAT&to=ST-FISH-HOEK&limit=abc")]
    [InlineData("/journeys?from=ST-RETREAT&to=ST-FISH-HOEK&limit=11")]
    [InlineData("/stations/ST-RETREAT/departures?after=24:00")]
    public async Task Invalid_requests_return_422(
        string path)
    {
        await using var t = await Start();

        var (status, body) = await t.Client.Get(path);

        Assert.Equal(422, status);
        Assert.True(body.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Unknown_train_is_404_with_error_shape()
    {
        await using var t = await Start();

        var (status, body) =
            await t.Client.Get("/trips/9999");

        Assert.Equal(404, status);

        Assert.Equal(
            "train_not_found",
            body.GetProperty("error").Str("code"));
    }

    // =====================================================
    // WEEKEND SERVICE CALENDAR
    // =====================================================

    [Theory]
    [InlineData(28)] // Saturday
    [InlineData(29)] // Sunday
    public async Task Weekend_excludes_weekday_services(
        int day)
    {
        await using var t = await Start(
            new DateTimeOffset(
                2026, 11, day, 3, 0, 0,
                TimeSpan.Zero));

        var (departureStatus, departures) =
            await t.Client.Get(
                "/stations/ST-RONDEBOSCH/departures" +
                "?after=05:00");

        Assert.Equal(200, departureStatus);
        Assert.Equal(0, departures.GetArrayLength());

        var (journeyStatus, journeys) =
            await t.Client.Get(
                "/journeys?from=ST-RONDEBOSCH" +
                "&to=ST-CLAREMONT&after=05:00");

        Assert.Equal(200, journeyStatus);
        Assert.Equal(0, journeys.GetArrayLength());
    }
}