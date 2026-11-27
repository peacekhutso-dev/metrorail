using MetroRail.Common;
using MetroRail.Tools;

namespace MetroRail.Tests;

public sealed class SimulationEngineTests
{
    // Monday, 23 November 2026.
    private static readonly DateTime Weekday =
        new(2026, 11, 23);

    private static DateTimeOffset At(
        int hour,
        int minute,
        int second = 0)
    {
        return new DateTimeOffset(
            Weekday
                .AddHours(hour)
                .AddMinutes(minute)
                .AddSeconds(second),
            Clock.SastOffset)
            .ToUniversalTime();
    }

    // Reduced timetable fixture.
    // Coordinates are deliberately simplified test values.
    private static SimulationTrip Southbound() => new(
        "0103",
        "LN-SOUTHERN",
        "southbound",
        "Weekday",
        [
            new(0, "ST-CAPE-TOWN", "Cape Town",
                "CAPE TOWN", 300, -33.90, 18.40),

            new(1, "ST-WOODSTOCK", "Woodstock",
                "WOODSTOCK", 303, -33.92, 18.44),

            new(17, "ST-RETREAT", "Retreat",
                "RETREAT (A)", 338, -34.05, 18.47),

            new(18, "ST-RETREAT", "Retreat",
                "RETREAT (D)", 339, -34.05, 18.47),

            new(19, "ST-STEENBERG", "Steenberg",
                "STEENBERG", 342, -34.07, 18.46),

            new(25, "ST-FISH-HOEK", "Fish Hoek",
                "FISH HOEK (A)", 363, -34.14, 18.43)
        ]);

    private static SimulationTrip Northbound() => new(
        "0100",
        "LN-SOUTHERN",
        "northbound",
        "Weekday",
        [
            new(0, "ST-SIMONS-TOWN", "Simon's Town",
                "SIMON'S TOWN", 265, -34.19, 18.43),

            new(1, "ST-FISH-HOEK", "Fish Hoek",
                "FISH HOEK (A)", 275, -34.14, 18.43),

            new(2, "ST-FISH-HOEK", "Fish Hoek",
                "FISH HOEK (D)", 276, -34.14, 18.43),

            new(3, "ST-CAPE-TOWN", "Cape Town",
                "CAPE TOWN", 348, -33.90, 18.40)
        ]);

    [Fact]
    public void Train_is_scheduled_before_departure()
    {
        var result = SimulationEngine.Calculate(
            Southbound(), At(4, 59));

        Assert.Equal("scheduled", result.State);
        Assert.Equal(0.0, result.Progress);
        Assert.Null(result.AtStationId);
    }

    [Fact]
    public void Train_activates_at_scheduled_origin()
    {
        var result = SimulationEngine.Calculate(
            Southbound(), At(5, 0));

        Assert.Equal("at_station", result.State);
        Assert.Equal("ST-CAPE-TOWN", result.AtStationId);
        Assert.Equal("ST-WOODSTOCK", result.NextStationId);
    }

    [Fact]
    public void Position_is_interpolated_between_stations()
    {
        var result = SimulationEngine.Calculate(
            Southbound(), At(5, 1, 30));

        Assert.Equal("moving", result.State);
        Assert.Equal(-33.91, result.Lat, 6);
        Assert.Equal(18.42, result.Lng, 6);

        Assert.Equal("ST-CAPE-TOWN", result.LastStationId);
        Assert.Equal("ST-WOODSTOCK", result.NextStationId);
        Assert.Null(result.AtStationId);
    }

    [Fact]
    public void Train_reaches_scheduled_station()
    {
        var result = SimulationEngine.Calculate(
            Southbound(), At(5, 3));

        Assert.Equal("at_station", result.State);
        Assert.Equal("ST-WOODSTOCK", result.AtStationId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(59)]
    public void Train_remains_at_retreat_during_dwell(
        int second)
    {
        var result = SimulationEngine.Calculate(
            Southbound(), At(5, 38, second));

        Assert.Equal("at_station", result.State);
        Assert.Equal("ST-RETREAT", result.AtStationId);
        Assert.Equal("ST-STEENBERG", result.NextStationId);

        Assert.Equal(-34.05, result.Lat, 6);
        Assert.Equal(18.47, result.Lng, 6);
    }

    [Fact]
    public void Train_leaves_retreat_after_departure()
    {
        var result = SimulationEngine.Calculate(
            Southbound(), At(5, 39, 1));

        Assert.Equal("moving", result.State);
        Assert.Equal("ST-RETREAT", result.LastStationId);
        Assert.Equal("ST-STEENBERG", result.NextStationId);
        Assert.Null(result.AtStationId);
    }

    [Fact]
    public void Train_moves_towards_steenberg()
    {
        var result = SimulationEngine.Calculate(
            Southbound(), At(5, 40, 30));

        Assert.Equal("moving", result.State);
        Assert.Equal(-34.06, result.Lat, 6);
        Assert.Equal(18.465, result.Lng, 6);
    }

    [Fact]
    public void Train_completes_at_actual_terminus()
    {
        var result = SimulationEngine.Calculate(
            Southbound(), At(6, 3));

        Assert.Equal("completed", result.State);
        Assert.Equal("ST-FISH-HOEK", result.AtStationId);
        Assert.Null(result.NextStationId);
        Assert.Equal(1.0, result.Progress);
    }

    [Fact]
    public void Completed_train_does_not_continue_moving()
    {
        var result = SimulationEngine.Calculate(
            Southbound(), At(6, 30));

        Assert.Equal("completed", result.State);
        Assert.Equal(-34.14, result.Lat, 6);
        Assert.Equal(18.43, result.Lng, 6);
    }

    [Fact]
    public void Northbound_train_activates_correctly()
    {
        var result = SimulationEngine.Calculate(
            Northbound(), At(4, 25));

        Assert.Equal("northbound", result.Direction);
        Assert.Equal("at_station", result.State);
        Assert.Equal("ST-SIMONS-TOWN", result.AtStationId);
        Assert.Equal("ST-FISH-HOEK", result.NextStationId);
    }

    [Fact]
    public void Northbound_train_moves_correctly()
    {
        var result = SimulationEngine.Calculate(
            Northbound(), At(4, 30));

        Assert.Equal("moving", result.State);
        Assert.Equal("northbound", result.Direction);
        Assert.Equal(-34.165, result.Lat, 6);
    }

    [Theory]
    [InlineData(28)]
    [InlineData(29)]
    public void Weekday_trains_do_not_operate_on_weekends(
        int day)
    {
        var weekend = new DateTimeOffset(
            2026, 11, day, 5, 15, 0,
            Clock.SastOffset);

        var result = SimulationEngine.Calculate(
            Southbound(), weekend);

        Assert.Equal("not_operating", result.State);
        Assert.Equal(0.0, result.Progress);
    }

    [Fact]
    public void Unsupported_service_calendar_is_inactive()
    {
        var trip = Southbound() with
        {
            ServiceDays = "Unknown"
        };

        var result = SimulationEngine.Calculate(
            trip, At(5, 15));

        Assert.Equal("not_operating", result.State);
    }

    [Fact]
    public void Simulated_position_has_explicit_source()
    {
        var result = SimulationEngine.Calculate(
            Southbound(), At(5, 15));

        Assert.Equal("simulator", result.Source);
    }

    [Fact]
    public void Position_timestamp_is_utc()
    {
        var sast = new DateTimeOffset(
            2026, 11, 23, 5, 15, 0,
            Clock.SastOffset);

        var result = SimulationEngine.Calculate(
            Southbound(), sast);

        Assert.Equal(TimeSpan.Zero, result.At.Offset);
        Assert.Equal(sast.ToUniversalTime(), result.At);
    }

    [Fact]
    public void Same_input_produces_same_position()
    {
        var trip = Southbound();
        var at = At(5, 20, 15);

        var first = SimulationEngine.Calculate(trip, at);
        var second = SimulationEngine.Calculate(trip, at);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Trip_with_insufficient_stops_is_rejected()
    {
        var trip = Southbound() with
        {
            Stops = [Southbound().Stops[0]]
        };

        Assert.Throws<ArgumentException>(
            () => SimulationEngine.Validate(trip));
    }

    [Fact]
    public void Non_chronological_timetable_is_rejected()
    {
        var trip = Southbound();
        var stops = trip.Stops.ToArray();

        stops[1] = stops[1] with
        {
            ScheduledMin = 299
        };

        Assert.Throws<ArgumentException>(
            () => SimulationEngine.Validate(
                trip with { Stops = stops }));
    }

    [Fact]
    public void Duplicate_event_sequence_is_rejected()
    {
        var trip = Southbound();
        var stops = trip.Stops.ToArray();

        stops[1] = stops[1] with
        {
            Seq = stops[0].Seq
        };

        Assert.Throws<ArgumentException>(
            () => SimulationEngine.Validate(
                trip with { Stops = stops }));
    }

    [Fact]
    public void Invalid_coordinates_are_rejected()
    {
        var trip = Southbound();
        var stops = trip.Stops.ToArray();

        stops[0] = stops[0] with
        {
            Lat = 100
        };

        Assert.Throws<ArgumentException>(
            () => SimulationEngine.Validate(
                trip with { Stops = stops }));
    }

    [Fact]
    public void Zero_duration_movement_is_rejected()
    {
        var trip = Southbound();
        var stops = trip.Stops.ToArray();

        stops[1] = stops[1] with
        {
            ScheduledMin = stops[0].ScheduledMin
        };

        Assert.Throws<ArgumentException>(
            () => SimulationEngine.Validate(
                trip with { Stops = stops }));
    }

    [Fact]
    public void Invalid_direction_is_rejected()
    {
        var trip = Southbound() with
        {
            Direction = "unknown"
        };

        Assert.Throws<ArgumentException>(
            () => SimulationEngine.Validate(trip));
    }

    [Fact]
    public void Non_finite_coordinates_are_rejected()
    {
        var trip = Southbound();
        var stops = trip.Stops.ToArray();

        stops[0] = stops[0] with
        {
            Lat = double.NaN
        };

        Assert.Throws<ArgumentException>(
            () => SimulationEngine.Validate(
                trip with { Stops = stops }));
    }
}