using System.Globalization;
using MetroRail.Common;

namespace MetroRail.Network;

/// <summary>
/// Centralised business rules for scheduled railway services.
/// </summary>
public static class TimetableRules
{
    /// <summary>
    /// Determines whether a scheduled event is an arrival-only event.
    /// </summary>
    public static bool IsArrivalOnly(ScheduledTripStop stop)
    {
        return stop.SourceStationLabel
            .Trim()
            .EndsWith("(A)", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether a scheduled event is a departure-only event.
    /// </summary>
    public static bool IsDepartureOnly(ScheduledTripStop stop)
    {
        return stop.SourceStationLabel
            .Trim()
            .EndsWith("(D)", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether passengers may board at this scheduled call.
    /// Passengers cannot board at arrival-only events or the terminus.
    /// </summary>
    public static bool CanBoard(
        ScheduledTripStop stop,
        ScheduledTripStop finalStop)
    {
        return !IsArrivalOnly(stop)
            && stop.Seq < finalStop.Seq
            && stop.StationId != finalStop.StationId;
    }

    /// <summary>
    /// Determines whether passengers may leave the train at this call.
    /// Departure-only events and the originating station are excluded.
    /// </summary>
    public static bool CanAlight(
        ScheduledTripStop stop,
        ScheduledTripStop firstStop)
    {
        return !IsDepartureOnly(stop)
            && stop.Seq > firstStop.Seq
            && stop.StationId != firstStop.StationId;
    }

    /// <summary>
    /// Validates a timetable query time in strict HH:mm format.
    /// Returns the number of minutes after midnight.
    /// </summary>
    public static int ParseQueryTime(string value)
    {
        if (!TimeOnly.TryParseExact(
                value,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time))
        {
            throw new ValidationException(
                "Time must use HH:mm format between 00:00 and 23:59.",
                new[] { "after" });
        }

        return time.Hour * 60 + time.Minute;
    }

    /// <summary>
    /// Determines whether a service operates on the supplied SAST date.
    /// Only weekday services are currently supported.
    /// </summary>
    public static bool OperatesOn(
        ScheduledTrip trip,
        DateTimeOffset utcNow)
    {
        var day = Clock.ToSast(utcNow).DayOfWeek;

        return trip.ServiceDays switch
        {
            "Weekday" => day != DayOfWeek.Saturday
                      && day != DayOfWeek.Sunday,

            // Unsupported service calendars are not advertised.
            _ => false
        };
    }
}