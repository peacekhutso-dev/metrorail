
using Microsoft.EntityFrameworkCore;

namespace MetroRail.Network;

// ========================================================
// RAILWAY NETWORK ENTITIES
// ========================================================

/// <summary>
/// Represents a physical railway station.
/// </summary>
public sealed class Station
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double Lat { get; set; }
    public double Lng { get; set; }
}

/// <summary>
/// Represents a railway line.
/// </summary>
public sealed class Line
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Colour { get; set; } = "";
}

/// <summary>
/// Represents the physical sequence of stations on a line.
/// </summary>
public sealed class LineStop
{
    public string LineId { get; set; } = "";
    public int Seq { get; set; }
    public string StationId { get; set; } = "";
    public int MinutesFromPrevious { get; set; }
}

// ========================================================
// TIMETABLE ENTITIES
// ========================================================

/// <summary>
/// Represents one scheduled train service.
/// Train numbers are strings to preserve leading zeroes.
/// </summary>
public sealed class ScheduledTrip
{
    public string TrainNo { get; set; } = "";
    public string LineId { get; set; } = "";

    // southbound or northbound.
    public string Direction { get; set; } = "";

    // Departure time from the service origin.
    public int DepartsMin { get; set; }

    public string Platform { get; set; } = "";

    // Initial timetable supports weekday services.
    public string ServiceDays { get; set; } = "Weekday";

    // Actual scheduled calls for this train.
    public ICollection<ScheduledTripStop> Stops { get; set; }
        = new List<ScheduledTripStop>();
}

/// <summary>
/// Represents an actual scheduled station call.
/// A dash in the source timetable does not create a record.
/// </summary>
public sealed class ScheduledTripStop
{
    public string TrainNo { get; set; } = "";

    public ScheduledTrip Trip { get; set; } = null!;

    public string StationId { get; set; } = "";

    public Station Station { get; set; } = null!;

    // Position in the original timetable.
    public int Seq { get; set; }

    // Scheduled time in minutes after midnight.
    public int ScheduledMin { get; set; }

    // Preserve the original timetable label.
    public string SourceStationLabel { get; set; } = "";
}

// ========================================================
// ENTITY FRAMEWORK DATABASE CONTEXT
// ========================================================

public sealed class NetworkDb(
    DbContextOptions<NetworkDb> o
) : DbContext(o)
{
    public DbSet<Station> Stations => Set<Station>();

    public DbSet<Line> Lines => Set<Line>();

    public DbSet<LineStop> LineStops => Set<LineStop>();

    public DbSet<ScheduledTrip> ScheduledTrips
        => Set<ScheduledTrip>();

    public DbSet<ScheduledTripStop> ScheduledTripStops
        => Set<ScheduledTripStop>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Station configuration.
        b.Entity<Station>().HasKey(x => x.Id);

        b.Entity<Station>().HasIndex(x => x.Name);

        // Railway line configuration.
        b.Entity<Line>().HasKey(x => x.Id);

        // Physical station sequence.
        b.Entity<LineStop>()
            .HasKey(x => new { x.LineId, x.Seq });

        // Scheduled train configuration.
        b.Entity<ScheduledTrip>()
            .HasKey(x => x.TrainNo);

        // One train can have multiple scheduled calls.
        b.Entity<ScheduledTrip>()
            .HasMany(t => t.Stops)
            .WithOne(s => s.Trip)
            .HasForeignKey(s => s.TrainNo)
            .OnDelete(DeleteBehavior.Cascade);

        // Preserve unique timetable row positions per train.
        b.Entity<ScheduledTripStop>()
            .HasKey(s => new { s.TrainNo, s.Seq });

        // Optimise station departure queries.
        b.Entity<ScheduledTripStop>()
            .HasIndex(s => new
            {
                s.StationId,
                s.ScheduledMin
            });

        // Each scheduled call references a physical station.
        b.Entity<ScheduledTripStop>()
            .HasOne(s => s.Station)
            .WithMany()
            .HasForeignKey(s => s.StationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

// ========================================================
// API RESPONSE MODELS
// ========================================================

public sealed record StationOut(
    string Id,
    string Name,
    double Lat,
    double Lng,
    IReadOnlyList<string> Lines
);

public sealed record LineOut(
    string Id,
    string Name,
    string Colour
);

public sealed record LineStopOut(
    int Seq,
    string StationId,
    string StationName,
    int MinutesFromOrigin
);

public sealed record StopTime(
    int Seq,
    string StationId,
    string StationName,
    string SourceStationLabel,
    string Scheduled
);

public sealed record TripOut(
    string TrainNo,
    string LineId,
    string Direction,
    string Terminus,
    string Platform,
    IReadOnlyList<StopTime> Stops
);

public sealed record DepartureOut(
    string TrainNo,
    string LineId,
    string Direction,
    string Terminus,
    string Platform,
    string SourceStationLabel,
    string Scheduled,
    int ScheduledMin,
    int InMin
);

public sealed record JourneyOption(
    string TrainNo,
    string LineId,
    string FromStation,
    string ToStation,
    string Departs,
    string Arrives,
    int DurationMin,
    string Platform,
    int Changes = 0
);
