using Microsoft.EntityFrameworkCore;

namespace MetroRail.Tracking;

/// <summary>Latest known state of a running train (one row per train).</summary>
public sealed class TrainLive
{
    public string TrainNo { get; set; } = "";
    public double Lat { get; set; }
    public double Lng { get; set; }
    public string Source { get; set; } = "gps"; // gps | staff_scan | crowd | simulator
    public DateTimeOffset SeenAt { get; set; }
    public DateTimeOffset LastMovedAt { get; set; }
    public string State { get; set; } = "moving";
    public string? LastStationId { get; set; }
    public string? NextStationId { get; set; }
    public string? AtStationId { get; set; }
    public int DelayMin { get; set; }
}

/// <summary>Actual arrival at a station — history for delays and for training the ETA model.</summary>
public sealed class StationPass
{
    public int Id { get; set; }
    public string TrainNo { get; set; } = "";
    public string StationId { get; set; } = "";
    public DateTimeOffset ArrivedAt { get; set; }
    public string Scheduled { get; set; } = "";
    public int DelayMin { get; set; }
}

public sealed class CrowdReport
{
    public int Id { get; set; }
    public string TrainNo { get; set; } = "";
    public string StationId { get; set; } = "";
    public string Kind { get; set; } = ""; // arrived | delayed | crowded | not_running
    public string? UserId { get; set; }
    public DateTimeOffset At { get; set; }
}

public sealed class TrackingDb(DbContextOptions<TrackingDb> o) : DbContext(o)
{
    public DbSet<TrainLive> Trains => Set<TrainLive>();
    public DbSet<StationPass> Passes => Set<StationPass>();
    public DbSet<CrowdReport> Reports => Set<CrowdReport>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<TrainLive>().HasKey(x => x.TrainNo);
        b.Entity<StationPass>().HasIndex(x => new { x.TrainNo, x.StationId });
        b.Entity<CrowdReport>().HasIndex(x => x.TrainNo);
    }
}

public sealed record PositionIn(string TrainNo, double Lat, double Lng, DateTimeOffset? At, string? Source);
public sealed record ReportIn(string TrainNo, string StationId, string Kind);
public sealed record TrainOut(string TrainNo, double Lat, double Lng, string State, int StationaryS, string? LastStationId,
    string? NextStationId, string? AtStationId, int DelayMin, DateTimeOffset SeenAt, string Source);
