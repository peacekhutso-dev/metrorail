using System.Text.Json;
using MetroRail.Common;
using Microsoft.EntityFrameworkCore;

namespace MetroRail.Notifications;

/// <summary>Local copy of saved trips that want alerts (built from trips.* events).</summary>
public sealed class TripWatch
{
    public string TripId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string TrainNo { get; set; } = "";
    public string Name { get; set; } = "";
    public string FromStationId { get; set; } = "";
    public string UsualTime { get; set; } = "";
}

public sealed class Alert
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Kind { get; set; } = ""; // delay | held | stopped | disruption | leave_now | info
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? TrainNo { get; set; }
    public string DedupeKey { get; set; } = "";
    public bool Read { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class NotificationsDb(DbContextOptions<NotificationsDb> o) : DbContext(o)
{
    public DbSet<TripWatch> Watches => Set<TripWatch>();
    public DbSet<Alert> Alerts => Set<Alert>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<TripWatch>().HasKey(x => x.TripId);
        b.Entity<TripWatch>().HasIndex(x => x.TrainNo);
        b.Entity<Alert>().HasKey(x => x.Id);
        b.Entity<Alert>().HasIndex(x => new { x.UserId, x.DedupeKey }).IsUnique();
    }
}

public sealed record AlertOut(string Id, string Kind, string Title, string Body, string? TrainNo, bool Read, string CreatedAt);
public sealed record AlertDraft(string Kind, string Title, string Body);

/// <summary>Which alert (if any) a train event produces for one saved trip. Pure, so the copy is testable.</summary>
public static class Rules
{
    public static string Name(string? stationId) => string.IsNullOrEmpty(stationId)
        ? "the next station"
        : string.Join(' ', stationId.Replace("ST-", "").Split('-').Select(w => w[..1] + w[1..].ToLowerInvariant()));

    private static string? S(JsonElement ev, string p) => ev.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int I(JsonElement ev, string p) => ev.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    public static AlertDraft? ForTrainEvent(JsonElement ev, string tripName, string fromStationId, string usualTime)
    {
        var state = S(ev, "state");
        var delay = I(ev, "delay_min");
        var train = S(ev, "train_no");
        var where = $"your {usualTime} from {Name(fromStationId)}";
        var mins = I(ev, "stationary_s") / 60;
        return state switch
        {
            "disruption" => new("disruption", "Possible disruption on your line",
                $"Train {train} ({tripName}) hasn't moved for {mins} min near {Name(S(ev, "last_station_id"))}. Check other ways to travel."),
            "stopped" => new("stopped", "Your train has stopped",
                $"Train {train} for {where} has stopped between {Name(S(ev, "last_station_id"))} and {Name(S(ev, "next_station_id"))} for {mins} min. Times will change."),
            "held" => new("held", "Your train is held",
                $"Train {train} for {where} is held at {Name(S(ev, "at_station_id") ?? S(ev, "last_station_id"))}."),
            _ when delay >= 2 => new("delay", "Your train is delayed", $"Train {train} for {where} is running {delay} min late."),
            _ => null,
        };
    }
}

public static class Endpoints
{
    public static void MapNotifications(this WebApplication app)
    {
        var g = app.MapGroup("/alerts").WithTags("notifications");

        g.MapGet("", async (HttpContext ctx, NotificationsDb db) =>
        {
            var u = await ctx.RequireUserAsync();
            var rows = await db.Alerts.Where(a => a.UserId == u.UserId).ToListAsync();
            return rows.OrderByDescending(a => a.CreatedAt).Take(50)
                .Select(a => new AlertOut(a.Id, a.Kind, a.Title, a.Body, a.TrainNo, a.Read, a.CreatedAt.ToString("O")));
        });

        g.MapGet("/unread-count", async (HttpContext ctx, NotificationsDb db) =>
        {
            var u = await ctx.RequireUserAsync();
            return new { unread = await db.Alerts.CountAsync(a => a.UserId == u.UserId && !a.Read) };
        });

        g.MapPost("/{id}/read", async (string id, HttpContext ctx, NotificationsDb db) =>
        {
            var u = await ctx.RequireUserAsync();
            var a = await db.Alerts.FindAsync(id);
            if (a is null || a.UserId != u.UserId) throw new NotFoundException("Alert not found", "alert_not_found");
            a.Read = true;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapPost("/read-all", async (HttpContext ctx, NotificationsDb db) =>
        {
            var u = await ctx.RequireUserAsync();
            await db.Alerts.Where(a => a.UserId == u.UserId).ExecuteUpdateAsync(s => s.SetProperty(a => a.Read, true));
            return Results.NoContent();
        });
    }

    // ---- event handlers ----
    public static void Subscribe(WebApplication app)
    {
        var bus = app.Services.GetRequiredService<IEventBus>();
        async Task WithDb(Func<NotificationsDb, Task> f)
        {
            using var scope = app.Services.CreateScope();
            await f(scope.ServiceProvider.GetRequiredService<NotificationsDb>());
        }

        bus.Subscribe("trips.trip.saved", (_, ev) => WithDb(async db =>
        {
            var id = ev.GetProperty("id").GetString()!;
            await db.Watches.Where(w => w.TripId == id).ExecuteDeleteAsync();
            var train = ev.TryGetProperty("train_no", out var tn) && tn.ValueKind == JsonValueKind.String ? tn.GetString() : null;
            if (ev.GetProperty("alert_delays").GetBoolean() && train is not null)
            {
                db.Watches.Add(new TripWatch
                {
                    TripId = id, UserId = ev.GetProperty("user_id").GetString()!, TrainNo = train, Name = ev.GetProperty("name").GetString()!,
                    FromStationId = ev.GetProperty("from_station_id").GetString()!, UsualTime = ev.GetProperty("usual_time").GetString()!,
                });
                await db.SaveChangesAsync();
            }
        }));

        bus.Subscribe("trips.trip.deleted", (_, ev) => WithDb(db =>
        {
            var id = ev.GetProperty("id").GetString();
            return db.Watches.Where(w => w.TripId == id).ExecuteDeleteAsync();
        }));

        bus.Subscribe("identity.user.deleted", (_, ev) => WithDb(async db =>
        {
            var uid = ev.GetProperty("user_id").GetString();
            await db.Watches.Where(w => w.UserId == uid).ExecuteDeleteAsync();
            await db.Alerts.Where(a => a.UserId == uid).ExecuteDeleteAsync();
        }));

        bus.Subscribe("tracking.train.state_changed", async (_, ev) =>
        {
            var clock = app.Services.GetRequiredService<IClock>();
            var created = new List<Alert>();
            await WithDb(async db =>
            {
                var train = ev.GetProperty("train_no").GetString();
                var today = Clock.ToSast(clock.UtcNow).ToString("yyyy-MM-dd");
                foreach (var w in await db.Watches.Where(w => w.TrainNo == train).ToListAsync())
                {
                    var draft = Rules.ForTrainEvent(ev, w.Name, w.FromStationId, w.UsualTime);
                    if (draft is null) continue;
                    var key = $"{today}:{train}:{draft.Kind}";
                    if (await db.Alerts.AnyAsync(a => a.UserId == w.UserId && a.DedupeKey == key)) continue;
                    var a = new Alert
                    {
                        Id = "ALR-" + Guid.NewGuid().ToString("N")[..10], UserId = w.UserId, Kind = draft.Kind, Title = draft.Title,
                        Body = draft.Body, TrainNo = train, DedupeKey = key, CreatedAt = clock.UtcNow,
                    };
                    db.Alerts.Add(a);
                    created.Add(a);
                }
                await db.SaveChangesAsync();
            });
            foreach (var a in created) // realtime pushes this to the user's in-app channel
                await bus.PublishAsync("notifications.alert.created",
                    new { user_id = a.UserId, id = a.Id, kind = a.Kind, title = a.Title, body = a.Body, train_no = a.TrainNo });
        });
    }
}

public static class NotificationsService
{
    public static WebApplicationBuilder CreateBuilder(string[] args, ServiceSettings? settings = null)
    {
        settings ??= new ServiceSettings("notifications");
        var b = ServiceHost.CreateBuilder(args, settings);
        b.Services.AddServiceDb<NotificationsDb>(settings);
        return b;
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        var app = ServiceHost.Build<NotificationsDb>(builder);
        Endpoints.Subscribe(app);
        app.MapNotifications();
        return app;
    }
}

public static class Program
{
    public static async Task Main(string[] args)
    {
        var app = NotificationsService.Build(NotificationsService.CreateBuilder(args));
        await ServiceHost.StartBusAsync(app);
        await app.RunAsync();
    }
}
