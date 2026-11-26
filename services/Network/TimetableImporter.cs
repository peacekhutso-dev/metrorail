
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace MetroRail.Network;

/// <summary>
/// Imports Southern Line weekday timetables from embedded CSV files.
/// Preserves train numbers, station events and original timetable labels.
/// </summary>
public static class TimetableImporter
{
    private const string LineId = "LN-SOUTHERN";

    private const string InboundResource =
        "southern_line_weekday_inbound.csv";

    private const string OutboundResource =
        "southern_line_weekday_outbound.csv";

    /// <summary>
    /// Imports both weekday timetables in a single database transaction.
    /// </summary>
    public static async Task<int> ImportAsync(
        NetworkDb db,
        CancellationToken cancellationToken = default)
    {
        // Load the existing physical railway stations.
        var stations = await db.Stations
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Resolve timetable names to existing station IDs.
        var stationMap = stations.ToDictionary(
            s => NormalizeStationName(s.Name),
            s => s.Id,
            StringComparer.OrdinalIgnoreCase);

        // Ensure the Southern Line exists.
        if (!await db.Lines.AnyAsync(
                l => l.Id == LineId,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Southern Line must be seeded before importing timetables.");
        }

        // Parse and validate both files before modifying the database.
        var inbound = Parse(
            InboundResource,
            "northbound",
            stationMap);

        var outbound = Parse(
            OutboundResource,
            "southbound",
            stationMap);

        var trips = inbound.Concat(outbound).ToList();

        // Validate the expected weekday service count.
        if (inbound.Count != 42 || outbound.Count != 42)
        {
            throw new InvalidDataException(
                "Expected 42 inbound and 42 outbound services.");
        }

        // Ensure train numbers are unique across both files.
        if (trips.Select(t => t.TrainNo).Distinct().Count()
            != trips.Count)
        {
            throw new InvalidDataException(
                "Duplicate train numbers detected.");
        }

        // Import both directions atomically.
        await using var transaction =
            await db.Database.BeginTransactionAsync(
                cancellationToken);

        // Only replace the imported Southern Line weekday services.
        var trainNumbers = trips
            .Select(t => t.TrainNo)
            .ToList();

        var existingTrips = await db.ScheduledTrips
            .Where(t => trainNumbers.Contains(t.TrainNo))
            .ToListAsync(cancellationToken);

        // Prevent overwriting unrelated services.
        if (existingTrips.Any(t =>
                t.LineId != LineId ||
                t.ServiceDays != "Weekday"))
        {
            throw new InvalidOperationException(
                "Train numbers conflict with existing services.");
        }

        // Existing stop records are removed through cascade deletion.
        db.ScheduledTrips.RemoveRange(existingTrips);

        await db.SaveChangesAsync(cancellationToken);

        // Insert the validated services and their scheduled events.
        await db.ScheduledTrips.AddRangeAsync(
            trips,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return trips.Count;
    }

    /// <summary>
    /// Parses one wide-format timetable CSV.
    /// </summary>
    private static List<ScheduledTrip> Parse(
        string resourceName,
        string direction,
        IReadOnlyDictionary<string, string> stationMap)
    {
        using var stream = typeof(TimetableImporter)
            .Assembly
            .GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException(
                $"Missing timetable resource: {resourceName}");

        using var reader = new StreamReader(stream);

        var header = reader.ReadLine()
            ?? throw new InvalidDataException(
                $"Empty timetable: {resourceName}");

        var columns = header.Split(',');

        if (columns.Length != 43 ||
            !columns[0].Trim().Equals(
                "station",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Invalid timetable header: {resourceName}");
        }

        // Create a train service for each timetable column.
        var trips = columns.Skip(1)
            .Select(number => new ScheduledTrip
            {
                TrainNo = number.Trim(),
                LineId = LineId,
                Direction = direction,
                ServiceDays = "Weekday",

                // Platform assignments have not been verified.
                Platform = ""
            })
            .ToList();

        // Validate train numbers without removing leading zeroes.
        if (trips.Any(t =>
                t.TrainNo.Length != 4 ||
                !t.TrainNo.All(char.IsAsciiDigit)))
        {
            throw new InvalidDataException(
                $"Invalid train number in {resourceName}");
        }

        string? row;
        var sequence = 0;

        while ((row = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(row))
                continue;

            var cells = row.Split(',');

            if (cells.Length != columns.Length)
            {
                throw new InvalidDataException(
                    $"Invalid column count in {resourceName}, " +
                    $"row {sequence + 2}.");
            }

            var sourceLabel = cells[0].Trim();

            var normalizedName =
                NormalizeStationName(sourceLabel);

            // Never create stations automatically from CSV labels.
            if (!stationMap.TryGetValue(
                    normalizedName,
                    out var stationId))
            {
                throw new InvalidDataException(
                    $"Unknown station: {sourceLabel}");
            }

            for (var i = 0; i < trips.Count; i++)
            {
                var value = cells[i + 1].Trim();

                // A dash means the train has no scheduled event here.
                if (value is "—" or "–" or "-")
                    continue;

                // Reject malformed or missing time values.
                if (!TimeOnly.TryParseExact(
                        value,
                        "HH:mm",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var time))
                {
                    throw new InvalidDataException(
                        $"Invalid time '{value}' for train " +
                        $"{trips[i].TrainNo} at {sourceLabel}.");
                }

                var scheduledMin =
                    time.Hour * 60 + time.Minute;

                trips[i].Stops.Add(new ScheduledTripStop
                {
                    TrainNo = trips[i].TrainNo,
                    StationId = stationId,
                    Seq = sequence,
                    ScheduledMin = scheduledMin,
                    SourceStationLabel = sourceLabel
                });
            }

            sequence++;
        }

        // Both supplied files contain 30 timetable rows.
        if (sequence != 30)
        {
            throw new InvalidDataException(
                $"Expected 30 timetable rows in {resourceName}.");
        }

        // Determine each train's actual origin time.
        foreach (var trip in trips)
        {
            var stops = trip.Stops
                .OrderBy(s => s.Seq)
                .ToList();

            if (stops.Select(s => s.StationId)
                    .Distinct().Count() < 2)
            {
                throw new InvalidDataException(
                    $"Train {trip.TrainNo} has insufficient stations.");
            }

            // Validate chronological ordering.
            for (var i = 1; i < stops.Count; i++)
            {
                if (stops[i].ScheduledMin <
                    stops[i - 1].ScheduledMin)
                {
                    throw new InvalidDataException(
                        $"Non-chronological timetable for train " +
                        $"{trip.TrainNo} at " +
                        $"{stops[i].SourceStationLabel}.");
                }
            }

            // Use the explicit departure event at the origin
            // when one is available.
            var origin = stops[0];

            var departure = stops.FirstOrDefault(s =>
                s.StationId == origin.StationId &&
                s.SourceStationLabel.EndsWith(
                    "(D)",
                    StringComparison.OrdinalIgnoreCase));

            trip.DepartsMin =
                departure?.ScheduledMin ?? origin.ScheduledMin;
        }

        return trips;
    }

    /// <summary>
    /// Converts source timetable labels into comparable station names.
    /// Original labels are retained in ScheduledTripStop.
    /// </summary>
    private static string NormalizeStationName(string name)
    {
        // Remove arrival and departure markers, spaces,
        // punctuation and differences in letter casing.
        var normalized = name
            .Trim()
            .ToUpperInvariant()
            .Replace("(A)", "")
            .Replace("(D)", "")
            .Replace(" ", "")
            .Replace(".", "")
            .Replace("'", "")
            .Replace("’", "");

        // Map alternative station names to canonical names.
        return normalized switch
        {
            "DIEPRIVIER" => "DIEPRIVER",
            _ => normalized
        };
    }
    }
