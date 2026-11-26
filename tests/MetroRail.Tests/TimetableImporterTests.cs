using MetroRail.Network;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MetroRail.Tests;

/// <summary>
/// Verifies timetable import accuracy, database integrity
/// and repeatability using an isolated database.
/// </summary>
public sealed class TimetableImporterTests
{
    private static Task<TestApp> Start() =>
        TestApp.StartAsync(
            NetworkService.CreateBuilder,
            NetworkService.Build,
            "network");

    [Fact]
    public async Task Import_creates_expected_timetable()
    {
        await using var t = await Start();

        using var scope = t.App.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<NetworkDb>();

        var imported =
            await TimetableImporter.ImportAsync(db);

        Assert.Equal(84, imported);

        Assert.Equal(28, await db.Stations.CountAsync());
        Assert.Equal(1, await db.Lines.CountAsync());
        Assert.Equal(28, await db.LineStops.CountAsync());

        Assert.Equal(
            84,
            await db.ScheduledTrips.CountAsync());

        Assert.Equal(
            2232,
            await db.ScheduledTripStops.CountAsync());

        Assert.Equal(
            42,
            await db.ScheduledTrips.CountAsync(
                x => x.Direction == "northbound"));

        Assert.Equal(
            42,
            await db.ScheduledTrips.CountAsync(
                x => x.Direction == "southbound"));

        Assert.Equal(
            84,
            await db.ScheduledTrips.CountAsync(
                x => x.ServiceDays == "Weekday"));
    }

    [Fact]
    public async Task Reimport_does_not_duplicate_or_change_events()
    {
        await using var t = await Start();

        using var scope = t.App.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<NetworkDb>();

        await TimetableImporter.ImportAsync(db);

        // Capture every imported event before reimporting.
        var before = await db.ScheduledTripStops
            .AsNoTracking()
            .OrderBy(x => x.TrainNo)
            .ThenBy(x => x.Seq)
            .Select(x => new
            {
                x.TrainNo,
                x.Seq,
                x.StationId,
                x.SourceStationLabel,
                x.ScheduledMin
            })
            .ToListAsync();

        // Clear tracked entities to simulate a fresh import.
        db.ChangeTracker.Clear();

        var imported =
            await TimetableImporter.ImportAsync(db);

        var after = await db.ScheduledTripStops
            .AsNoTracking()
            .OrderBy(x => x.TrainNo)
            .ThenBy(x => x.Seq)
            .Select(x => new
            {
                x.TrainNo,
                x.Seq,
                x.StationId,
                x.SourceStationLabel,
                x.ScheduledMin
            })
            .ToListAsync();

        Assert.Equal(84, imported);

        Assert.Equal(
            84,
            await db.ScheduledTrips.CountAsync());

        Assert.Equal(2232, after.Count);

        // Verify values, not just the number of records.
        Assert.Equal(before.ToArray(), after.ToArray());
    }

    [Fact]
    public async Task Imported_trips_have_valid_stop_sequences()
    {
        await using var t = await Start();

        using var scope = t.App.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<NetworkDb>();

        await TimetableImporter.ImportAsync(db);

        var trips = await db.ScheduledTrips
            .AsNoTracking()
            .Include(x => x.Stops)
            .ToListAsync();

        Assert.Equal(84, trips.Count);

        foreach (var trip in trips)
        {
            var stops = trip.Stops
                .OrderBy(x => x.Seq)
                .ToArray();

            Assert.True(stops.Length >= 2);

            Assert.True(
                stops.Select(x => x.StationId)
                    .Distinct()
                    .Count() >= 2);

            Assert.Equal(
                stops.Length,
                stops.Select(x => x.Seq)
                    .Distinct()
                    .Count());

            Assert.All(stops, stop =>
            {
                Assert.InRange(
                    stop.ScheduledMin, 0, 1439);
            });

            // Scheduled times must never move backwards.
            for (var i = 1; i < stops.Length; i++)
            {
                Assert.True(
                    stops[i].ScheduledMin >=
                    stops[i - 1].ScheduledMin,
                    $"Invalid chronology for {trip.TrainNo}");
            }
        }
    }
}