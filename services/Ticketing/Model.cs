using Microsoft.EntityFrameworkCore;

namespace MetroRail.Ticketing;

/// <summary>Ticket products. Prices are SAMPLE values for the demo — real fares come from PRASA's fare tables.</summary>
public sealed record Product(string Id, string Name, string Description, int PriceCents, int? Trips);

public static class Products
{
    public static readonly Dictionary<string, Product> All = new()
    {
        ["single"] = new("single", "Single trip", "One journey today, changes included.", 1200, 1),
        ["ten_trip"] = new("ten_trip", "10-trip", "One trip per journey, not per scan. Valid 60 days.", 9500, 10),
        ["weekly"] = new("weekly", "Weekly", "Unlimited on this route for 7 days.", 11000, null),
        ["monthly"] = new("monthly", "Monthly", "Unlimited on this route until month end.", 42000, null),
    };

    public static readonly Dictionary<int, int> TopUps = new() { [10] = 9500, [20] = 18500 };

    public static (DateTimeOffset From, DateTimeOffset To) Validity(string productId, DateTimeOffset start)
    {
        var local = Common.Clock.ToSast(start);
        var dayStart = new DateTimeOffset(local.Date, Common.Clock.SastOffset);
        return productId switch
        {
            "single" => (start, dayStart.AddDays(1)),
            "ten_trip" => (start, dayStart.AddDays(61)),
            "weekly" => (start, dayStart.AddDays(7)),
            _ => (start, new DateTimeOffset(new DateTime(local.Year, local.Month, 1).AddMonths(1), Common.Clock.SastOffset)),
        };
    }

    /// <summary>How long one trip stays open after the first scan (covers changes and re-scans):
    /// scheduled journey x 1.5 + 30 min per change, between 90 min and 4 h.</summary>
    public static int JourneyWindowMin(int scheduledMin, int changes = 0) =>
        Math.Clamp((int)Math.Round(scheduledMin * 1.5 + 30 * changes, MidpointRounding.ToEven), 90, 240);
}

public sealed class Purchase
{
    public string Id { get; set; } = "";          // PUR-xxxx
    public string UserId { get; set; } = "";
    public string Kind { get; set; } = "new";     // new | topup
    public string ProductId { get; set; } = "";
    public string FromStationId { get; set; } = "";
    public string ToStationId { get; set; } = "";
    public string DevicePublicKey { get; set; } = "";
    public int AmountCents { get; set; }
    public string Status { get; set; } = "pending"; // pending | paid | failed
    public string? TicketId { get; set; }
    public int TopupTrips { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Ticket
{
    public string Id { get; set; } = "";          // MR-48213
    public string UserId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string FromStationId { get; set; } = "";
    public string ToStationId { get; set; } = "";
    public string LineId { get; set; } = "";
    public int? TripsTotal { get; set; }
    public int TripsUsed { get; set; }
    public DateTimeOffset? OpenTripUntil { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset ValidTo { get; set; }
    public int ScheduledMin { get; set; } = 7;
    public string Cert { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Nonces already scanned, so a photo or copy of a code can't be used twice.</summary>
public sealed class SeenCode
{
    public int Id { get; set; }
    public string TicketId { get; set; } = "";
    public string Nonce { get; set; } = "";
    public long Slot { get; set; }
}

public sealed class Validation
{
    public int Id { get; set; }
    public string? TicketId { get; set; }
    public string ScannerId { get; set; } = "";
    public string TrainNo { get; set; } = "";
    public string Result { get; set; } = "";
    public string? Reason { get; set; }
    public int? TripNo { get; set; }
    public DateTimeOffset At { get; set; }
}

public sealed class TicketingDb(DbContextOptions<TicketingDb> o) : DbContext(o)
{
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<SeenCode> SeenCodes => Set<SeenCode>();
    public DbSet<Validation> Validations => Set<Validation>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Purchase>().HasKey(x => x.Id);
        b.Entity<Purchase>().HasIndex(x => x.UserId);
        b.Entity<Ticket>().HasKey(x => x.Id);
        b.Entity<Ticket>().HasIndex(x => x.UserId);
        b.Entity<SeenCode>().HasIndex(x => new { x.TicketId, x.Nonce }).IsUnique();
        b.Entity<Validation>().HasIndex(x => x.TicketId);
    }
}

public sealed record PurchaseIn(string ProductId, string FromStationId, string ToStationId, string DevicePublicKey);
public sealed record PurchaseOut(string Id, string Status, int AmountCents, string Description, string? TicketId);
public sealed record TopUpIn(int Trips);
public sealed record TicketOut(string Id, string ProductId, string ProductName, string FromStationId, string ToStationId, int? TripsTotal,
    int TripsUsed, int? TripsLeft, bool TripInProgress, DateTimeOffset? TripValidUntil, DateTimeOffset ValidTo, string Status, string Cert);
public sealed record ValidateIn(string Qr, string TrainNo, string ScannerId, string LineId = "LN-SOUTHERN");
public sealed record ValidateOut(string Result, string? Reason = null, string? TicketId = null, string? ProductName = null,
    string? Route = null, int? TripNo = null, int? TripsLeft = null, DateTimeOffset? ValidUntil = null, bool NewTripStarted = false);
