namespace MetroRail.Common;

/// <summary>
/// Times: store UTC, display Africa/Johannesburg (SAST, UTC+2, no DST).
///
/// DEMO_TIME=07:14 makes the service behave as if it is 07:14 SAST today when it started, then the
/// clock runs normally — so the pitch shows the morning peak at any hour. Security timestamps
/// (token expiry, QR 30-second slots) must use <see cref="Clock.RealUtcNow"/>, never this.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class Clock : IClock
{
    public static readonly TimeSpan SastOffset = TimeSpan.FromHours(2);
    private readonly DateTimeOffset? _base;
    private readonly System.Diagnostics.Stopwatch _since = System.Diagnostics.Stopwatch.StartNew();

    public Clock(string? demoTime = null)
    {
        demoTime ??= Environment.GetEnvironmentVariable("DEMO_TIME");
        if (string.IsNullOrWhiteSpace(demoTime)) return;
        var parts = demoTime.Split(':');
        var sastToday = DateTimeOffset.UtcNow.ToOffset(SastOffset).Date;
        _base = new DateTimeOffset(sastToday.AddHours(int.Parse(parts[0])).AddMinutes(int.Parse(parts[1])), SastOffset)
            .ToUniversalTime();
    }

    public DateTimeOffset UtcNow => _base is { } b ? b + _since.Elapsed : DateTimeOffset.UtcNow;

    public static DateTimeOffset RealUtcNow => DateTimeOffset.UtcNow;

    public static DateTimeOffset ToSast(DateTimeOffset t) => t.ToOffset(SastOffset);

    public static int MinuteOfDay(DateTimeOffset t)
    {
        var s = ToSast(t);
        return s.Hour * 60 + s.Minute;
    }

    public static string Hhmm(int minuteOfDay)
    {
        var m = ((minuteOfDay % 1440) + 1440) % 1440;
        return $"{m / 60:00}:{m % 60:00}";
    }

    public static int ParseHhmm(string hhmm) => int.Parse(hhmm[..2]) * 60 + int.Parse(hhmm[3..5]);
}

/// <summary>Fixed clock for tests.</summary>
public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}
