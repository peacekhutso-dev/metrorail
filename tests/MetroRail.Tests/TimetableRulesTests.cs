using MetroRail.Common;
using MetroRail.Network;

namespace MetroRail.Tests;

/// <summary>
/// Regression tests for scheduled railway business rules.
/// </summary>
public sealed class TimetableRulesTests
{
    // Creates an isolated scheduled event for unit testing.
    private static ScheduledTripStop CreateStop(
        string stationId,
        string label,
        int seq,
        int scheduledMin = 0)
    {
        return new ScheduledTripStop
        {
            TrainNo = "0103",
            StationId = stationId,
            SourceStationLabel = label,
            Seq = seq,
            ScheduledMin = scheduledMin
        };
    }

    // =====================================================
    // ARRIVAL AND DEPARTURE CLASSIFICATION
    // =====================================================

    [Theory]
    [InlineData("RETREAT (A)", true)]
    [InlineData("FISH HOEK (A)", true)]
    [InlineData("retreat (a)", true)]
    [InlineData("RETREAT (A) ", true)]
    [InlineData("RETREAT (D)", false)]
    [InlineData("CAPE TOWN", false)]
    public void IsArrivalOnly_ClassifiesCorrectly(
        string label,
        bool expected)
    {
        var stop = CreateStop("ST-TEST", label, 1);

        Assert.Equal(
            expected,
            TimetableRules.IsArrivalOnly(stop));
    }

    [Theory]
    [InlineData("RETREAT (D)", true)]
    [InlineData("FISH HOEK (D)", true)]
    [InlineData("retreat (d)", true)]
    [InlineData("RETREAT (D) ", true)]
    [InlineData("RETREAT (A)", false)]
    [InlineData("CAPE TOWN", false)]
    public void IsDepartureOnly_ClassifiesCorrectly(
        string label,
        bool expected)
    {
        var stop = CreateStop("ST-TEST", label, 1);

        Assert.Equal(
            expected,
            TimetableRules.IsDepartureOnly(stop));
    }

    // =====================================================
    // BOARDING RULES
    // =====================================================

    [Fact]
    public void CanBoard_RejectsArrivalOnlyEvent()
    {
        var arrival = CreateStop(
            "ST-RETREAT", "RETREAT (A)", 17, 338);

        var terminus = CreateStop(
            "ST-FISH-HOEK", "FISH HOEK (A)", 25, 363);

        Assert.False(
            TimetableRules.CanBoard(arrival, terminus));
    }

    [Fact]
    public void CanBoard_AllowsDepartureEvent()
    {
        var departure = CreateStop(
            "ST-RETREAT", "RETREAT (D)", 18, 339);

        var terminus = CreateStop(
            "ST-FISH-HOEK", "FISH HOEK (A)", 25, 363);

        Assert.True(
            TimetableRules.CanBoard(departure, terminus));
    }

    [Fact]
    public void CanBoard_AllowsOrdinaryStation()
    {
        var station = CreateStop(
            "ST-WYNBERG", "WYNBERG", 11, 326);

        var terminus = CreateStop(
            "ST-FISH-HOEK", "FISH HOEK (A)", 25, 363);

        Assert.True(
            TimetableRules.CanBoard(station, terminus));
    }

    [Fact]
    public void CanBoard_AllowsOriginatingStation()
    {
        var origin = CreateStop(
            "ST-CAPE-TOWN", "CAPE TOWN", 0, 300);

        var terminus = CreateStop(
            "ST-FISH-HOEK", "FISH HOEK (A)", 25, 363);

        Assert.True(
            TimetableRules.CanBoard(origin, terminus));
    }

    [Fact]
    public void CanBoard_RejectsFinalEvent()
    {
        var terminus = CreateStop(
            "ST-FISH-HOEK", "FISH HOEK", 25, 363);

        Assert.False(
            TimetableRules.CanBoard(terminus, terminus));
    }

    [Fact]
    public void CanBoard_RejectsEarlierEventAtTerminus()
    {
        var earlierEvent = CreateStop(
            "ST-FISH-HOEK", "FISH HOEK (D)", 25, 363);

        var finalEvent = CreateStop(
            "ST-FISH-HOEK", "FISH HOEK", 26, 364);

        Assert.False(
            TimetableRules.CanBoard(
                earlierEvent, finalEvent));
    }

    // =====================================================
    // ALIGHTING RULES
    // =====================================================

    [Fact]
    public void CanAlight_AllowsArrivalEvent()
    {
        var origin = CreateStop(
            "ST-CAPE-TOWN", "CAPE TOWN", 0, 300);

        var arrival = CreateStop(
            "ST-RETREAT", "RETREAT (A)", 17, 338);

        Assert.True(
            TimetableRules.CanAlight(arrival, origin));
    }

    [Fact]
    public void CanAlight_RejectsDepartureOnlyEvent()
    {
        var origin = CreateStop(
            "ST-CAPE-TOWN", "CAPE TOWN", 0, 300);

        var departure = CreateStop(
            "ST-RETREAT", "RETREAT (D)", 18, 339);

        Assert.False(
            TimetableRules.CanAlight(departure, origin));
    }

    [Fact]
    public void CanAlight_AllowsOrdinaryStation()
    {
        var origin = CreateStop(
            "ST-CAPE-TOWN", "CAPE TOWN", 0, 300);

        var station = CreateStop(
            "ST-WYNBERG", "WYNBERG", 11, 326);

        Assert.True(
            TimetableRules.CanAlight(station, origin));
    }

    [Fact]
    public void CanAlight_RejectsOriginatingStation()
    {
        var origin = CreateStop(
            "ST-CAPE-TOWN", "CAPE TOWN", 0, 300);

        Assert.False(
            TimetableRules.CanAlight(origin, origin));
    }

    [Fact]
    public void CanAlight_AllowsTerminatingStation()
    {
        var origin = CreateStop(
            "ST-CAPE-TOWN", "CAPE TOWN", 0, 300);

        var terminus = CreateStop(
            "ST-FISH-HOEK", "FISH HOEK (A)", 25, 363);

        Assert.True(
            TimetableRules.CanAlight(terminus, origin));
    }

    // =====================================================
    // QUERY TIME VALIDATION
    // =====================================================

    [Theory]
    [InlineData("00:00", 0)]
    [InlineData("05:00", 300)]
    [InlineData("05:38", 338)]
    [InlineData("05:39", 339)]
    [InlineData("12:00", 720)]
    [InlineData("23:59", 1439)]
    public void ParseQueryTime_AcceptsValidTimes(
        string value,
        int expected)
    {
        Assert.Equal(
            expected,
            TimetableRules.ParseQueryTime(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("24:00")]
    [InlineData("25:00")]
    [InlineData("05:75")]
    [InlineData("5:30")]
    [InlineData("05:30:00")]
    [InlineData("-1:30")]
    [InlineData(" 05:30 ")]
    public void ParseQueryTime_RejectsInvalidTimes(
        string value)
    {
        Assert.Throws<ValidationException>(() =>
        {
            TimetableRules.ParseQueryTime(value);
        });
    }

    // =====================================================
    // SERVICE CALENDAR VALIDATION
    // =====================================================

    [Theory]
    [InlineData(23, true)]  // Monday
    [InlineData(24, true)]  // Tuesday
    [InlineData(25, true)]  // Wednesday
    [InlineData(26, true)]  // Thursday
    [InlineData(27, true)]  // Friday
    [InlineData(28, false)] // Saturday
    [InlineData(29, false)] // Sunday
    public void OperatesOn_EnforcesWeekdayCalendar(
        int day,
        bool expected)
    {
        var trip = new ScheduledTrip
        {
            ServiceDays = "Weekday"
        };

        var utcTime = new DateTimeOffset(
            2026, 11, day, 10, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            expected,
            TimetableRules.OperatesOn(trip, utcTime));
    }

    [Fact]
    public void OperatesOn_RejectsUnsupportedCalendar()
    {
        var trip = new ScheduledTrip
        {
            ServiceDays = "Unknown"
        };

        var utcTime = new DateTimeOffset(
            2026, 11, 23, 10, 0, 0, TimeSpan.Zero);

        Assert.False(
            TimetableRules.OperatesOn(trip, utcTime));
    }

    // =====================================================
    // SOUTH AFRICAN TIME ZONE BOUNDARIES
    // =====================================================

    [Fact]
    public void OperatesOn_FridayUtcButSaturdaySast()
    {
        var trip = new ScheduledTrip
        {
            ServiceDays = "Weekday"
        };

        // Friday 22:30 UTC = Saturday 00:30 SAST.
        var utcTime = new DateTimeOffset(
            2026, 11, 27, 22, 30, 0, TimeSpan.Zero);

        Assert.False(
            TimetableRules.OperatesOn(trip, utcTime));
    }

    [Fact]
    public void OperatesOn_SundayUtcButMondaySast()
    {
        var trip = new ScheduledTrip
        {
            ServiceDays = "Weekday"
        };

        // Sunday 22:30 UTC = Monday 00:30 SAST.
        var utcTime = new DateTimeOffset(
            2026, 11, 29, 22, 30, 0, TimeSpan.Zero);

        Assert.True(
            TimetableRules.OperatesOn(trip, utcTime));
    }
}