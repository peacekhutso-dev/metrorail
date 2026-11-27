using MetroRail.Common;

namespace MetroRail.Tools;

/// <summary>
/// A scheduled station event with geographical coordinates.
/// Arrival and departure events remain separate.
/// </summary>
public sealed record SimulationStop(
    int Seq,
    string StationId,
    string StationName,
    string SourceStationLabel,
    int ScheduledMin,
    double Lat,
    double Lng);

/// <summary>
/// A scheduled train service.
/// </summary>
public sealed record SimulationTrip(
    string TrainNo,
    string LineId,
    string Direction,
    string ServiceDays,
    IReadOnlyList<SimulationStop> Stops);

/// <summary>
/// Calculated train position at a specific simulation time.
/// </summary>
public sealed record SimulationPosition(
    string TrainNo,
    string LineId,
    string Direction,
    double Lat,
    double Lng,
    string State,
    string? LastStationId,
    string? NextStationId,
    string? AtStationId,
    DateTimeOffset At,
    double Progress,
    string Source = "simulator");

/// <summary>
/// Deterministic timetable-driven simulation engine.
///
/// This component does not perform HTTP requests,
/// database operations or event publishing.
/// </summary>
public static class SimulationEngine
{
    public const string Scheduled = "scheduled";
    public const string Moving = "moving";
    public const string AtStation = "at_station";
    public const string Completed = "completed";
    public const string NotOperating = "not_operating";

    /// <summary>
    /// Validate the supplied timetable before simulation.
    /// </summary>
    public static void Validate(SimulationTrip trip)
    {
        ArgumentNullException.ThrowIfNull(trip);

        if (string.IsNullOrWhiteSpace(trip.TrainNo))
            throw new ArgumentException(
                "Train number is required.");

        if (string.IsNullOrWhiteSpace(trip.LineId))
            throw new ArgumentException(
                "Railway line is required.");

        if (trip.Direction is not ("northbound" or "southbound"))
            throw new ArgumentException(
                "Invalid train direction.");

        if (trip.Stops is null || trip.Stops.Count < 2)
            throw new ArgumentException(
                "A train requires at least two scheduled events.");

        var stops = trip.Stops
            .OrderBy(s => s.Seq)
            .ToList();

        if (stops.Select(s => s.Seq).Distinct().Count()
            != stops.Count)
        {
            throw new ArgumentException(
                "Duplicate scheduled event sequence.");
        }

        if (stops.Select(s => s.StationId).Distinct().Count() < 2)
        {
            throw new ArgumentException(
                "A train requires at least two physical stations.");
        }

        for (var i = 0; i < stops.Count; i++)
        {
            var stop = stops[i];

            if (string.IsNullOrWhiteSpace(stop.StationId))
                throw new ArgumentException(
                    "Station identifier is required.");

            if (stop.ScheduledMin is < 0 or > 1439)
                throw new ArgumentException(
                    "Scheduled time is outside the valid range.");

            if (!double.IsFinite(stop.Lat) ||
                !double.IsFinite(stop.Lng) ||
                stop.Lat is < -90 or > 90 ||
                stop.Lng is < -180 or > 180)
            {
                throw new ArgumentException(
                    $"Invalid coordinates for {stop.StationId}.");
            }

            if (i > 0)
            {
                var previous = stops[i - 1];

                if (stop.ScheduledMin < previous.ScheduledMin)
                {
                    throw new ArgumentException(
                        "Scheduled events must be chronological.");
                }

                // A train cannot move between different
                // stations in zero elapsed time.
                if (stop.StationId != previous.StationId &&
                    stop.ScheduledMin == previous.ScheduledMin)
                {
                    throw new ArgumentException(
                        "Different stations cannot have identical times.");
                }
            }
        }
    }

    /// <summary>
    /// Determine whether the train operates on the supplied
    /// South African calendar date.
    /// </summary>
    public static bool OperatesOn(
        SimulationTrip trip,
        DateTimeOffset utcNow)
    {
        var day = Clock.ToSast(utcNow).DayOfWeek;

        return trip.ServiceDays switch
        {
            "Weekday" => day != DayOfWeek.Saturday &&
                         day != DayOfWeek.Sunday,

            _ => false
        };
    }

    /// <summary>
    /// Calculate the train's position using absolute time.
    /// No previous position is required.
    /// </summary>
    public static SimulationPosition Calculate(
        SimulationTrip trip,
        DateTimeOffset utcNow)
    {
        Validate(trip);

        var stops = trip.Stops
            .OrderBy(s => s.Seq)
            .ToList();

        var first = stops[0];
        var last = stops[^1];

        var sastNow = Clock.ToSast(utcNow);

        // Preserve seconds and milliseconds for smooth movement.
        var currentMin = sastNow.TimeOfDay.TotalMinutes;

        var duration = last.ScheduledMin - first.ScheduledMin;

        var progress = Math.Clamp(
            (currentMin - first.ScheduledMin) / duration,
            0.0,
            1.0);

        // The service does not operate on this calendar date.
        if (!OperatesOn(trip, utcNow))
        {
            return CreatePosition(
                trip,
                first.Lat,
                first.Lng,
                NotOperating,
                null,
                first.StationId,
                null,
                utcNow,
                0);
        }

        // The service has not departed.
        if (currentMin < first.ScheduledMin)
        {
            return CreatePosition(
                trip,
                first.Lat,
                first.Lng,
                Scheduled,
                null,
                first.StationId,
                null,
                utcNow,
                0);
        }

        // The train has reached its actual terminus.
        if (currentMin >= last.ScheduledMin)
        {
            return CreatePosition(
                trip,
                last.Lat,
                last.Lng,
                Completed,
                last.StationId,
                null,
                last.StationId,
                utcNow,
                1);
        }

        // Find the latest scheduled event reached.
        var index = 0;

        for (var i = 0; i < stops.Count; i++)
        {
            if (stops[i].ScheduledMin > currentMin)
                break;

            index = i;
        }

        var previous = stops[index];
        var next = stops[index + 1];

        var nextPhysicalStation = FindNextPhysicalStation(
            stops,
            index);

        // Consecutive events at the same physical station
        // represent a scheduled dwell interval.
        if (previous.StationId == next.StationId)
        {
            return CreatePosition(
                trip,
                previous.Lat,
                previous.Lng,
                AtStation,
                previous.StationId,
                nextPhysicalStation,
                previous.StationId,
                utcNow,
                progress);
        }

        // The train is exactly at a scheduled station event.
        if (currentMin == previous.ScheduledMin)
        {
            return CreatePosition(
                trip,
                previous.Lat,
                previous.Lng,
                AtStation,
                previous.StationId,
                nextPhysicalStation,
                previous.StationId,
                utcNow,
                progress);
        }

        // Calculate movement between consecutive stations.
        var segmentDuration =
            next.ScheduledMin - previous.ScheduledMin;

        var segmentProgress = Math.Clamp(
            (currentMin - previous.ScheduledMin) /
            segmentDuration,
            0.0,
            1.0);

        // Linear geographical interpolation.
        var lat = previous.Lat +
            (next.Lat - previous.Lat) * segmentProgress;

        var lng = previous.Lng +
            (next.Lng - previous.Lng) * segmentProgress;

        return CreatePosition(
            trip,
            lat,
            lng,
            Moving,
            previous.StationId,
            next.StationId,
            null,
            utcNow,
            progress);
    }

    /// <summary>
    /// Find the next distinct physical station.
    /// Prevents Retreat arrival -> Retreat departure
    /// from being interpreted as movement to another station.
    /// </summary>
    private static string? FindNextPhysicalStation(
        IReadOnlyList<SimulationStop> stops,
        int currentIndex)
    {
        var currentStation = stops[currentIndex].StationId;

        for (var i = currentIndex + 1; i < stops.Count; i++)
        {
            if (stops[i].StationId != currentStation)
                return stops[i].StationId;
        }

        return null;
    }

    private static SimulationPosition CreatePosition(
        SimulationTrip trip,
        double lat,
        double lng,
        string state,
        string? lastStationId,
        string? nextStationId,
        string? atStationId,
        DateTimeOffset at,
        double progress)
    {
        return new SimulationPosition(
            trip.TrainNo,
            trip.LineId,
            trip.Direction,
            lat,
            lng,
            state,
            lastStationId,
            nextStationId,
            atStationId,
            at.ToUniversalTime(),
            progress);
    }
}