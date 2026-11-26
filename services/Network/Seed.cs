
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace MetroRail.Network;

/// <summary>
/// Seeds the physical railway network.
/// Timetables are managed separately by TimetableImporter.
/// </summary>
public static class Seed
{
    /// <summary>
    /// Loads the embedded railway network definition.
    /// </summary>
    public static JsonElement Load()
    {
        using var stream = typeof(Seed)
            .Assembly
            .GetManifestResourceStream("southern_line.json")
            ?? throw new FileNotFoundException(
                "Southern Line network resource not found.");

        using var document = JsonDocument.Parse(stream);

        return document.RootElement.Clone();
    }

    /// <summary>
    /// Creates the physical railway network on first startup.
    /// Does not generate synthetic train services.
    /// </summary>
    public static async Task<bool> SeedIfEmptyAsync(
        NetworkDb db)
    {
        // Do not overwrite an existing railway network.
        if (await db.Stations.AnyAsync())
            return false;

        var data = Load();

        // Create physical railway stations.
        foreach (var station in
                 data.GetProperty("stations").EnumerateArray())
        {
            db.Stations.Add(new Station
            {
                Id = station.GetProperty("id").GetString()!,
                Name = station.GetProperty("name").GetString()!,
                Lat = station.GetProperty("lat").GetDouble(),
                Lng = station.GetProperty("lng").GetDouble()
            });
        }

        // Create railway lines and station sequences.
        foreach (var line in
                 data.GetProperty("lines").EnumerateArray())
        {
            var lineId = line.GetProperty("id").GetString()!;

            db.Lines.Add(new Line
            {
                Id = lineId,
                Name = line.GetProperty("name").GetString()!,
                Colour = line.GetProperty("colour").GetString()!
            });

            foreach (var stop in
                     line.GetProperty("stops").EnumerateArray())
            {
                db.LineStops.Add(new LineStop
                {
                    LineId = lineId,
                    Seq = stop.GetProperty("seq").GetInt32(),
                    StationId = stop.GetProperty("station_id")
                        .GetString()!,
                    MinutesFromPrevious = stop
                        .GetProperty("minutes_from_previous")
                        .GetInt32()
                });
            }
        }

        // Persist the physical railway network.
        await db.SaveChangesAsync();

        return true;
    }
}
