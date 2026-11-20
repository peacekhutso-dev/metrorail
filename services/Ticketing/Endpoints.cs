using System.Security.Cryptography;
using System.Text.Json.Nodes;
using MetroRail.Common;
using Microsoft.EntityFrameworkCore;

namespace MetroRail.Ticketing;

public static class Endpoints
{
    private static PurchaseOut Out(Purchase p) => new(p.Id, p.Status, p.AmountCents,
        p.Kind == "new" ? Products.All[p.ProductId].Name : $"Top up {p.TopupTrips} trips", p.TicketId);

    // Billing keeps the amount from this event, so the app can never choose its own price.
    private static Task Announce(IEventBus bus, Purchase p) => bus.PublishAsync("ticketing.purchase.created",
        new { purchase_id = p.Id, user_id = p.UserId, amount_cents = p.AmountCents, description = Out(p).Description });

    public static TicketOut TicketOut(Ticket t, DateTimeOffset now)
    {
        var open = t.OpenTripUntil is { } u && u > now;
        int? left = t.TripsTotal is null ? null : t.TripsTotal - t.TripsUsed;
        var status = t.ValidTo <= now ? "expired"
            : open ? "in_progress"
            : left == 0 ? "used_up"
            : left is not null && left <= 2 && t.TripsTotal > 1 ? "low"
            : "ready";
        return new TicketOut(t.Id, t.ProductId, Products.All[t.ProductId].Name, t.FromStationId, t.ToStationId, t.TripsTotal, t.TripsUsed,
            left, open, open ? t.OpenTripUntil : null, t.ValidTo, status, t.Cert);
    }

    private static JsonObject CertPayload(Ticket t, string dpk) => new()
    {
        ["v"] = 1, ["tid"] = t.Id, ["uid"] = t.UserId, ["prd"] = t.ProductId, ["from"] = t.FromStationId, ["to"] = t.ToStationId,
        ["line"] = t.LineId, ["trips"] = t.TripsTotal, ["vf"] = t.ValidFrom.ToUnixTimeSeconds(), ["vt"] = t.ValidTo.ToUnixTimeSeconds(), ["dpk"] = dpk,
    };

    private static async Task<Ticket> OwnTicket(TicketingDb db, string id, Principal user)
    {
        var t = await db.Tickets.FindAsync(id);
        return t is null || t.UserId != user.UserId ? throw new NotFoundException("Ticket not found", "ticket_not_found") : t;
    }

    public static void MapTicketing(this WebApplication app)
    {
        var g = app.MapGroup("").WithTags("ticketing");

        g.MapGet("/products", () => Products.All.Values);

        // Public keys for scanners and apps: verify certificates, seal QR codes.
        g.MapGet("/keys", (TicketCrypto.Keys keys) => keys.Public());

        g.MapPost("/purchases", async (PurchaseIn body, HttpContext ctx, TicketingDb db, IEventBus bus, IClock clock) =>
        {
            var u = await ctx.RequireUserAsync();
            var product = Products.All.GetValueOrDefault(body.ProductId) ?? throw new NotFoundException("Unknown ticket type", "product_not_found");
            if (body.FromStationId == body.ToStationId) throw new AppException("Pick two different stations", "same_station");
            Check.Required(body.DevicePublicKey, "device_public_key");
            var p = new Purchase
            {
                Id = "PUR-" + Guid.NewGuid().ToString("N")[..10], UserId = u.UserId, ProductId = product.Id, FromStationId = body.FromStationId,
                ToStationId = body.ToStationId, DevicePublicKey = body.DevicePublicKey, AmountCents = product.PriceCents, CreatedAt = clock.UtcNow,
            };
            db.Purchases.Add(p);
            await db.SaveChangesAsync();
            await Announce(bus, p);
            return Results.Json(Out(p), Json.Options, statusCode: 201);
        });

        g.MapPost("/tickets/{ticketId}/topup", async (string ticketId, TopUpIn body, HttpContext ctx, TicketingDb db, IEventBus bus, IClock clock) =>
        {
            var u = await ctx.RequireUserAsync();
            var t = await OwnTicket(db, ticketId, u);
            if (t.TripsTotal is null) throw new AppException("This pass is already unlimited", "not_topupable");
            if (!Products.TopUps.TryGetValue(body.Trips, out var price)) throw new AppException("Top up 10 or 20 trips", "invalid_topup");
            var p = new Purchase
            {
                Id = "PUR-" + Guid.NewGuid().ToString("N")[..10], UserId = u.UserId, Kind = "topup", ProductId = t.ProductId,
                FromStationId = t.FromStationId, ToStationId = t.ToStationId, TicketId = t.Id, AmountCents = price, TopupTrips = body.Trips, CreatedAt = clock.UtcNow,
            };
            db.Purchases.Add(p);
            await db.SaveChangesAsync();
            await Announce(bus, p);
            return Results.Json(Out(p), Json.Options, statusCode: 201);
        });

        g.MapGet("/purchases/{id}", async (string id, HttpContext ctx, TicketingDb db) =>
        {
            var u = await ctx.RequireUserAsync();
            var p = await db.Purchases.FindAsync(id);
            return p is null || p.UserId != u.UserId ? throw new NotFoundException("Purchase not found", "purchase_not_found") : Out(p);
        });

        g.MapGet("/tickets", async (HttpContext ctx, TicketingDb db, IClock clock) =>
        {
            var u = await ctx.RequireUserAsync();
            return (await db.Tickets.Where(t => t.UserId == u.UserId).ToListAsync()).OrderByDescending(t => t.CreatedAt).Select(t => TicketOut(t, clock.UtcNow));
        });

        g.MapGet("/tickets/{id}", async (string id, HttpContext ctx, TicketingDb db, IClock clock) =>
            TicketOut(await OwnTicket(db, id, await ctx.RequireUserAsync()), clock.UtcNow));

        // Conductor scan (online). The scanner app runs the same checks offline and syncs every 60 s.
        g.MapPost("/validations", async (ValidateIn body, HttpContext ctx, TicketingDb db, IEventBus bus, IClock clock, TicketCrypto.Keys keys) =>
        {
            await ctx.RequireRoleAsync("conductor");
            Check.Matches(body.ScannerId, @"^SC-\d{6}$", "scanner_id");
            var now = clock.UtcNow;

            async Task<ValidateOut> Reject(string reason, string? tid = null)
            {
                db.ChangeTracker.Clear();
                db.Validations.Add(new Validation { TicketId = tid, ScannerId = body.ScannerId, TrainNo = body.TrainNo, Result = "rejected", Reason = reason, At = now });
                await db.SaveChangesAsync();
                return new ValidateOut("rejected", reason, tid);
            }

            TicketCrypto.ScannedQr scanned;
            try { scanned = TicketCrypto.ReadQr(keys, body.Qr); } // real clock (anti-replay), never DEMO_TIME
            catch (TicketException e) { return await Reject(e.Reason); }

            var tid = scanned.Cert["tid"]!.GetValue<string>();
            var t = await db.Tickets.FindAsync(tid);
            if (t is null) return await Reject("invalid_ticket", tid);
            if (TicketCrypto.CertPayload(t.Cert).ToJsonString() != scanned.Cert.ToJsonString())
                return await Reject("replaced_ticket", tid); // e.g. an old certificate from before a top-up

            if (await db.SeenCodes.AnyAsync(c => c.TicketId == tid && c.Nonce == scanned.Nonce)) return await Reject("already_scanned", tid);
            db.SeenCodes.Add(new SeenCode { TicketId = tid, Nonce = scanned.Nonce, Slot = scanned.Slot });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { return await Reject("already_scanned", tid); } // two scanners at the same instant

            if (!(t.ValidFrom <= now && now < t.ValidTo)) return await Reject("outside_validity", tid);
            if (t.LineId != body.LineId) return await Reject("wrong_route", tid);

            var newTrip = false;
            if (!(t.OpenTripUntil is { } open && open > now))
            {
                if (t.TripsTotal is not null && t.TripsUsed >= t.TripsTotal) return await Reject("no_trips_left", tid);
                if (t.TripsTotal is not null) t.TripsUsed++;
                t.OpenTripUntil = now.AddMinutes(Products.JourneyWindowMin(t.ScheduledMin));
                newTrip = true;
            }
            db.Validations.Add(new Validation { TicketId = tid, ScannerId = body.ScannerId, TrainNo = body.TrainNo, Result = "valid", TripNo = t.TripsUsed, At = now });
            await db.SaveChangesAsync();
            if (newTrip) await bus.PublishAsync("ticketing.trip.started", new { ticket_id = tid, train_no = body.TrainNo, trip_no = t.TripsUsed });
            int? left = t.TripsTotal is null ? null : t.TripsTotal - t.TripsUsed;
            return new ValidateOut("valid", null, tid, Products.All[t.ProductId].Name, $"{t.FromStationId} → {t.ToStationId}",
                t.TripsUsed, left, t.OpenTripUntil, newTrip);
        });
    }

    // ---- events ----
    public static void Subscribe(WebApplication app)
    {
        var bus = app.Services.GetRequiredService<IEventBus>();
        var keys = app.Services.GetRequiredService<TicketCrypto.Keys>();

        // billing.payment.succeeded -> issue the ticket (or add trips). Idempotent.
        bus.Subscribe("billing.payment.succeeded", async (_, ev) =>
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketingDb>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var p = await db.Purchases.FindAsync(ev.GetProperty("purchase_id").GetString());
            if (p is null || p.Status == "paid") return;
            p.Status = "paid";
            Ticket t;
            if (p.Kind == "topup")
            {
                t = (await db.Tickets.FindAsync(p.TicketId))!;
                t.TripsTotal = (t.TripsTotal ?? 0) + p.TopupTrips;
                var payload = TicketCrypto.CertPayload(t.Cert);
                payload["trips"] = t.TripsTotal;
                t.Cert = TicketCrypto.IssueCert(keys.Issuer, payload);
            }
            else
            {
                var (vf, vt) = Products.Validity(p.ProductId, clock.UtcNow);
                t = new Ticket
                {
                    Id = $"MR-{RandomNumberGenerator.GetInt32(10000, 100000)}", UserId = p.UserId, ProductId = p.ProductId,
                    FromStationId = p.FromStationId, ToStationId = p.ToStationId, LineId = "LN-SOUTHERN",
                    TripsTotal = Products.All[p.ProductId].Trips, ValidFrom = vf, ValidTo = vt, CreatedAt = clock.UtcNow,
                };
                t.Cert = TicketCrypto.IssueCert(keys.Issuer, CertPayload(t, p.DevicePublicKey));
                db.Tickets.Add(t);
                p.TicketId = t.Id;
            }
            await db.SaveChangesAsync();
            await bus.PublishAsync("ticketing.ticket.issued", new { ticket_id = t.Id, user_id = t.UserId, purchase_id = p.Id });
        });

        bus.Subscribe("billing.payment.failed", async (_, ev) =>
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketingDb>();
            if (await db.Purchases.FindAsync(ev.GetProperty("purchase_id").GetString()) is { Status: "pending" } p)
            {
                p.Status = "failed";
                await db.SaveChangesAsync();
            }
        });
    }
}

public static class TicketingService
{
    public static WebApplicationBuilder CreateBuilder(string[] args, ServiceSettings? settings = null)
    {
        settings ??= new ServiceSettings("ticketing");
        var s = settings;
        // ISSUER_KEY: Ed25519 private key PEM; SCANNER_KEY: X25519 private key PEM (shared with scanners).
        if (s.IsProduction && (string.IsNullOrWhiteSpace(s.Get("ISSUER_KEY")) || string.IsNullOrWhiteSpace(s.Get("SCANNER_KEY"))))
            throw new InvalidOperationException("ISSUER_KEY and SCANNER_KEY are required in production");
        var b = ServiceHost.CreateBuilder(args, s);
        b.Services.AddServiceDb<TicketingDb>(s);
        b.Services.AddSingleton(TicketCrypto.Keys.LoadOrGenerate(s.Get("ISSUER_KEY"), s.Get("SCANNER_KEY")));
        return b;
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        var app = ServiceHost.Build<TicketingDb>(builder);
        Endpoints.Subscribe(app);
        app.MapTicketing();
        return app;
    }
}

public static class Program
{
    public static async Task Main(string[] args)
    {
        var app = TicketingService.Build(TicketingService.CreateBuilder(args));
        await ServiceHost.StartBusAsync(app);
        await app.RunAsync();
    }
}
