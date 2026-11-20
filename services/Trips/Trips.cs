using MetroRail.Common;
using Microsoft.EntityFrameworkCore;

namespace MetroRail.Trips;

public sealed class SavedTrip
{
    public string Id { get; set; } = "";       // TRP-xxxx
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string FromStationId { get; set; } = "";
    public string ToStationId { get; set; } = "";
    public string Days { get; set; } = "";     // "Mon,Tue,..."
    public string UsualTime { get; set; } = ""; // departure from FromStation, HH:MM
    public string? TrainNo { get; set; }
    public bool AlertDelays { get; set; } = true;
    public bool LeaveReminder { get; set; } = true;
    public int LeaveMinutes { get; set; } = 10;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TripsDb(DbContextOptions<TripsDb> o) : DbContext(o)
{
    public DbSet<SavedTrip> Trips => Set<SavedTrip>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SavedTrip>().HasKey(x => x.Id);
        b.Entity<SavedTrip>().HasIndex(x => x.UserId);
    }
}

public sealed record TripIn(string Name, string FromStationId, string ToStationId, List<string> Days, string UsualTime,
    string? TrainNo, bool AlertDelays = true, bool LeaveReminder = true, int LeaveMinutes = 10);
public sealed record TripOut(string Id, string Name, string FromStationId, string ToStationId, List<string> Days, string UsualTime,
    string? TrainNo, bool AlertDelays, bool LeaveReminder, int LeaveMinutes);

public static class Endpoints
{
    private static readonly HashSet<string> Days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
    private const int MaxTrips = 10;

    private static TripOut Out(SavedTrip t) => new(t.Id, t.Name, t.FromStationId, t.ToStationId, t.Days.Split(',').ToList(),
        t.UsualTime, t.TrainNo, t.AlertDelays, t.LeaveReminder, t.LeaveMinutes);

    private static object Event(SavedTrip t) => new
    {
        id = t.Id, user_id = t.UserId, name = t.Name, from_station_id = t.FromStationId, to_station_id = t.ToStationId,
        days = t.Days.Split(','), usual_time = t.UsualTime, train_no = t.TrainNo, alert_delays = t.AlertDelays,
        leave_reminder = t.LeaveReminder, leave_minutes = t.LeaveMinutes,
    };

    private static TripIn Validate(TripIn b)
    {
        var name = Check.Required(b.Name?.Trim(), "name");
        Check.That(name.Length <= 30, "name", "Keep the name under 30 characters");
        Check.That(b.Days is { Count: > 0 } && b.Days.All(Days.Contains), "days", "Pick at least one day");
        Check.Matches(b.UsualTime, "^([01][0-9]|2[0-3]):[0-5][0-9]$", "usual_time");
        Check.Range(b.LeaveMinutes, 0, 60, "leave_minutes");
        if (b.FromStationId == b.ToStationId) throw new AppException("Pick two different stations", "same_station");
        return b with { Name = name };
    }

    private static void Apply(SavedTrip t, TripIn b)
    {
        (t.Name, t.FromStationId, t.ToStationId, t.Days, t.UsualTime, t.TrainNo) =
            (b.Name, b.FromStationId, b.ToStationId, string.Join(',', b.Days), b.UsualTime, b.TrainNo);
        (t.AlertDelays, t.LeaveReminder, t.LeaveMinutes) = (b.AlertDelays, b.LeaveReminder, b.LeaveMinutes);
    }

    private static async Task<SavedTrip> Own(TripsDb db, string id, Principal user)
    {
        var t = await db.Trips.FindAsync(id);
        return t is null || t.UserId != user.UserId ? throw new NotFoundException("Trip not found", "trip_not_found") : t;
    }

    public static void MapTrips(this WebApplication app)
    {
        var g = app.MapGroup("/trips").WithTags("trips");

        g.MapGet("", async (HttpContext ctx, TripsDb db) =>
        {
            var u = await ctx.RequireUserAsync();
            return (await db.Trips.Where(t => t.UserId == u.UserId).ToListAsync()).OrderBy(t => t.UsualTime, StringComparer.Ordinal).Select(Out);
        });

        g.MapPost("", async (TripIn body, HttpContext ctx, TripsDb db, IEventBus bus, IClock clock) =>
        {
            var u = await ctx.RequireUserAsync();
            body = Validate(body);
            if (await db.Trips.CountAsync(t => t.UserId == u.UserId) >= MaxTrips)
                throw new AppException($"You can save up to {MaxTrips} trips", "too_many_trips");
            var t = new SavedTrip { Id = "TRP-" + Guid.NewGuid().ToString("N")[..10], UserId = u.UserId, CreatedAt = clock.UtcNow };
            Apply(t, body);
            db.Trips.Add(t);
            await db.SaveChangesAsync();
            await bus.PublishAsync("trips.trip.saved", Event(t));
            return Results.Json(Out(t), Json.Options, statusCode: 201);
        });

        g.MapPut("/{id}", async (string id, TripIn body, HttpContext ctx, TripsDb db, IEventBus bus) =>
        {
            var t = await Own(db, id, await ctx.RequireUserAsync());
            Apply(t, Validate(body));
            await db.SaveChangesAsync();
            await bus.PublishAsync("trips.trip.saved", Event(t));
            return Out(t);
        });

        g.MapDelete("/{id}", async (string id, HttpContext ctx, TripsDb db, IEventBus bus) =>
        {
            var u = await ctx.RequireUserAsync();
            var t = await Own(db, id, u);
            db.Trips.Remove(t);
            await db.SaveChangesAsync();
            await bus.PublishAsync("trips.trip.deleted", new { id, user_id = u.UserId });
            return Results.NoContent();
        });
    }
}

public static class TripsService
{
    public static WebApplicationBuilder CreateBuilder(string[] args, ServiceSettings? settings = null)
    {
        settings ??= new ServiceSettings("trips");
        var b = ServiceHost.CreateBuilder(args, settings);
        b.Services.AddServiceDb<TripsDb>(settings);
        return b;
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        var app = ServiceHost.Build<TripsDb>(builder);
        app.Services.GetRequiredService<IEventBus>().Subscribe("identity.user.deleted", async (_, ev) =>
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TripsDb>();
            var userId = ev.GetProperty("user_id").GetString();
            await db.Trips.Where(t => t.UserId == userId).ExecuteDeleteAsync();
        });
        app.MapTrips();
        return app;
    }
}

public static class Program
{
    public static async Task Main(string[] args)
    {
        var app = TripsService.Build(TripsService.CreateBuilder(args));
        await ServiceHost.StartBusAsync(app);
        await app.RunAsync();
    }
}
