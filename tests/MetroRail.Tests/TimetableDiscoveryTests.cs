using System.Text.Json;
using MetroRail.Common;
using MetroRail.Network;
using MetroRail.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace MetroRail.Tests;

public sealed class TimetableDiscoveryTests
{
    private const string LineId = "LN-SOUTHERN";

    private const string Endpoint =
        "/lines/LN-SOUTHERN/trips";

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
            using var scope =
                app.App.Services.CreateScope();

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

    private static async Task<JsonElement> LoadCatalogue(
        TestApp app)
    {
        var (status, body) =
            await app.Client.Get(Endpoint);

        Assert.Equal(200, status);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);

        return body;
    }

    [Fact]
    public async Task Catalogue_returns_all_84_services()
    {
        await using var app = await Start();

        var body = await LoadCatalogue(app);

        Assert.Equal(84, body.GetArrayLength());
    }

    [Fact]
    public async Task Catalogue_contains_both_directions()
    {
        await using var app = await Start();

        var body = await LoadCatalogue(app);

        var trips = body.EnumerateArray().ToArray();

        Assert.Equal(42, trips.Count(
            t => t.Str("direction") == "northbound"));

        Assert.Equal(42, trips.Count(
            t => t.Str("direction") == "southbound"));
    }

    [Fact]
    public async Task Catalogue_contains_all_scheduled_events()
    {
        await using var app = await Start();

        var body = await LoadCatalogue(app);

        var count = body.EnumerateArray()
            .Sum(t => t.GetProperty("stops")
                .GetArrayLength());

        Assert.Equal(2232, count);
    }

    [Fact]
    public async Task Catalogue_preserves_operating_calendars()
    {
        await using var app = await Start();

        var body = await LoadCatalogue(app);

        Assert.All(
            body.EnumerateArray(),
            trip => Assert.Equal(
                "Weekday",
                trip.Str("service_days")));
    }

    [Fact]
    public async Task Catalogue_preserves_train_numbers()
    {
        await using var app = await Start();

        var body = await LoadCatalogue(app);

        var trainNumbers = body.EnumerateArray()
            .Select(t => t.Str("train_no"))
            .ToArray();

        Assert.Contains("0103", trainNumbers);
        Assert.Contains("0100", trainNumbers);

        Assert.Equal(
            trainNumbers.Length,
            trainNumbers.Distinct().Count());
    }

    [Fact]
    public async Task Train_0103_has_correct_terminus()
    {
        await using var app = await Start();

        var body = await LoadCatalogue(app);

        var trip = Assert.Single(
            body.EnumerateArray(),
            t => t.Str("train_no") == "0103");

        Assert.Equal("Fish Hoek", trip.Str("terminus"));

        var stops = trip.GetProperty("stops")
            .EnumerateArray()
            .ToArray();

        Assert.Equal(26, stops.Length);

        Assert.Equal(
            "ST-CAPE-TOWN",
            stops[0].Str("station_id"));

        Assert.Equal(
            "ST-FISH-HOEK",
            stops[^1].Str("station_id"));

        Assert.Equal(
            300,
            stops[0].GetProperty("scheduled_min")
                .GetInt32());

        Assert.Equal(
            363,
            stops[^1].GetProperty("scheduled_min")
                .GetInt32());
    }

    [Fact]
    public async Task Catalogue_preserves_retreat_dwell_events()
    {
        await using var app = await Start();

        var body = await LoadCatalogue(app);

        var trip = Assert.Single(
            body.EnumerateArray(),
            t => t.Str("train_no") == "0103");

        var retreat = trip.GetProperty("stops")
            .EnumerateArray()
            .Where(s =>
                s.Str("station_id") == "ST-RETREAT")
            .ToArray();

        Assert.Equal(2, retreat.Length);

        Assert.Equal(
            "RETREAT (A)",
            retreat[0].Str("source_station_label"));

        Assert.Equal(
            "RETREAT (D)",
            retreat[1].Str("source_station_label"));

        Assert.Equal(
            338,
            retreat[0].GetProperty("scheduled_min")
                .GetInt32());

        Assert.Equal(
            339,
            retreat[1].GetProperty("scheduled_min")
                .GetInt32());
    }

    [Fact]
    public async Task Catalogue_contains_valid_coordinates()
    {
        await using var app = await Start();

        var body = await LoadCatalogue(app);

        foreach (var trip in body.EnumerateArray())
        {
            foreach (var stop in trip
                .GetProperty("stops")
                .EnumerateArray())
            {
                var lat = stop.GetProperty("lat")
                    .GetDouble();

                var lng = stop.GetProperty("lng")
                    .GetDouble();

                Assert.True(double.IsFinite(lat));
                Assert.True(double.IsFinite(lng));

                Assert.InRange(lat, -90.0, 90.0);
                Assert.InRange(lng, -180.0, 180.0);

                Assert.False(lat == 0 && lng == 0);
            }
        }
    }

    [Fact]
    public async Task Catalogue_is_ordered_deterministically()
    {
        await using var app = await Start();

        var body = await LoadCatalogue(app);

        var trainNumbers = body.EnumerateArray()
            .Select(t => t.Str("train_no"))
            .ToArray();

        Assert.Equal(
            trainNumbers.OrderBy(
                t => t,
                StringComparer.Ordinal).ToArray(),
            trainNumbers);

        foreach (var trip in body.EnumerateArray())
        {
            var sequences = trip.GetProperty("stops")
                .EnumerateArray()
                .Select(s => s.GetProperty("seq")
                    .GetInt32())
                .ToArray();

            Assert.Equal(
                sequences.OrderBy(s => s).ToArray(),
                sequences);

            Assert.Equal(
                sequences.Length,
                sequences.Distinct().Count());
        }
    }

    [Fact]
    public async Task Unknown_line_returns_404()
    {
        await using var app = await Start();

        var (status, body) = await app.Client.Get(
            "/lines/LN-UNKNOWN/trips");

        Assert.Equal(404, status);

        Assert.Equal(
            "line_not_found",
            body.GetProperty("error").Str("code"));
    }

    [Fact]
    public async Task Catalogue_remains_available_on_weekends()
    {
        // Saturday, 28 November 2026.
        await using var app = await Start(
            new DateTimeOffset(
                2026, 11, 28, 3, 0, 0,
                TimeSpan.Zero));

        var body = await LoadCatalogue(app);

        Assert.Equal(84, body.GetArrayLength());
    }

    [Fact]
    public async Task Client_loads_all_imported_train_services()
    {
        await using var app = await Start();

        var client = new TimetableClient(app.Client);

        var trips = await client.LoadAsync(LineId);

        Assert.Equal(84, trips.Count);

        Assert.Equal(
            2232,
            trips.Sum(t => t.Stops.Count));
    }

    [Fact]
    public async Task Imported_timetable_drives_simulation()
    {
        await using var app = await Start();

        var client = new TimetableClient(app.Client);

        var trips = await client.LoadAsync(LineId);

        var trip = Assert.Single(
            trips,
            t => t.TrainNo == "0103");

        var at = new DateTimeOffset(
            2026, 11, 23, 5, 38, 30,
            Clock.SastOffset);

        var position = SimulationEngine.Calculate(
            trip,
            at);

        Assert.Equal(
            SimulationEngine.AtStation,
            position.State);

        Assert.Equal(
            "ST-RETREAT",
            position.AtStationId);

        Assert.Equal(
            "ST-STEENBERG",
            position.NextStationId);

        Assert.Equal("simulator", position.Source);
    }

    [Fact]
    public async Task All_imported_services_can_be_simulated()
    {
        await using var app = await Start();

        var client = new TimetableClient(app.Client);

        var trips = await client.LoadAsync(LineId);

        var day = new DateTimeOffset(
            2026, 11, 23, 0, 0, 0,
            Clock.SastOffset);

        foreach (var trip in trips)
        {
            var ordered = trip.Stops
                .OrderBy(s => s.Seq)
                .ToArray();

            var first = ordered[0];
            var last = ordered[^1];

            var departure = day.AddMinutes(
                first.ScheduledMin);

            var arrival = day.AddMinutes(
                last.ScheduledMin);

            var startPosition =
                SimulationEngine.Calculate(
                    trip,
                    departure);

            var endPosition =
                SimulationEngine.Calculate(
                    trip,
                    arrival);

            Assert.Equal(
                SimulationEngine.AtStation,
                startPosition.State);

            Assert.Equal(
                first.StationId,
                startPosition.AtStationId);

            Assert.Equal(
                SimulationEngine.Completed,
                endPosition.State);

            Assert.Equal(
                last.StationId,
                endPosition.AtStationId);

            Assert.Null(endPosition.NextStationId);
        }
    }

    [Fact]
    public async Task Imported_services_are_inactive_on_weekends()
    {
        await using var app = await Start();

        var client = new TimetableClient(app.Client);

        var trips = await client.LoadAsync(LineId);

        var saturday = new DateTimeOffset(
            2026, 11, 28, 5, 30, 0,
            Clock.SastOffset);

        foreach (var trip in trips)
        {
            var position = SimulationEngine.Calculate(
                trip,
                saturday);

            Assert.Equal(
                SimulationEngine.NotOperating,
                position.State);
        }
    }

    [Fact]
    public async Task Client_rejects_unknown_line()
    {
        await using var app = await Start();

        var client = new TimetableClient(app.Client);

        var exception =
            await Assert.ThrowsAsync<HttpRequestException>(
                () => client.LoadAsync("LN-UNKNOWN"));

        Assert.Equal(
            System.Net.HttpStatusCode.NotFound,
            exception.StatusCode);
    }
}