using Microsoft.EntityFrameworkCore;

namespace MetroRail.Network;

public sealed class Station
{
    public string Id { get; set; } = "";   // e.g. ST-RONDEBOSCH
    public string Name { get; set; } = "";
    public double Lat { get; set; }
    public double Lng { get; set; }
}

public sealed class Line
{
    public string Id { get; set; } = "";   // e.g. LN-SOUTHERN
    public string Name { get; set; } = "";
    public string Colour { get; set; } = "";
}

public sealed class LineStop
{
    public string LineId { get; set; } = "";
    public int Seq { get; set; }
    public string StationId { get; set; } = "";
    public int MinutesFromPrevious { get; set; }
}

/// <summary>One timetabled run, e.g. train 3412 southbound leaving Cape Town at 07:01.</summary>
public sealed class ScheduledTrip
{
    public string TrainNo { get; set; } = "";
    public string LineId { get; set; } = "";
    public string Direction { get; set; } = "";  // southbound | northbound
    public int DepartsMin { get; set; }           // minutes after midnight SAST, from origin
    public string Platform { get; set; } = "";
}

public sealed class NetworkDb(DbContextOptions<NetworkDb> o) : DbContext(o)
{
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Line> Lines => Set<Line>();
    public DbSet<LineStop> LineStops => Set<LineStop>();
    public DbSet<ScheduledTrip> ScheduledTrips => Set<ScheduledTrip>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Station>().HasKey(x => x.Id);
        b.Entity<Station>().HasIndex(x => x.Name);
        b.Entity<Line>().HasKey(x => x.Id);
        b.Entity<LineStop>().HasKey(x => new { x.LineId, x.Seq });
        b.Entity<ScheduledTrip>().HasKey(x => x.TrainNo);
    }
}

// ---- API shapes (snake_case on the wire) ----
public sealed record StationOut(string Id, string Name, double Lat, double Lng, IReadOnlyList<string> Lines);
public sealed record LineOut(string Id, string Name, string Colour);
public sealed record LineStopOut(int Seq, string StationId, string StationName, int MinutesFromOrigin);
public sealed record StopTime(int Seq, string StationId, string StationName, string Scheduled);
public sealed record TripOut(string TrainNo, string LineId, string Direction, string Terminus, string Platform, IReadOnlyList<StopTime> Stops);
public sealed record DepartureOut(string TrainNo, string LineId, string Direction, string Terminus, string Platform,
    string Scheduled, int ScheduledMin, int InMin);
public sealed record JourneyOption(string TrainNo, string LineId, string FromStation, string ToStation, string Departs,
    string Arrives, int DurationMin, string Platform, int Changes = 0);
