using System.Text.Json;
using MetroRail.Common;
using Microsoft.EntityFrameworkCore;

namespace MetroRail.Billing;

/// <summary>Amount owed for a purchase (from ticketing.purchase.created). The app never sends amounts.</summary>
public sealed class Charge
{
    public string PurchaseId { get; set; } = "";
    public string UserId { get; set; } = "";
    public int AmountCents { get; set; }
    public string Description { get; set; } = "";
}

public sealed class Payment
{
    public string Id { get; set; } = "";       // PAY-xxxx
    public string PurchaseId { get; set; } = "";
    public string UserId { get; set; } = "";
    public int AmountCents { get; set; }
    public string Description { get; set; } = "";
    public string GatewayRef { get; set; } = "";
    public string Status { get; set; } = "";   // requires_card | requires_3ds | succeeded | declined
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? DeclineReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SavedCard
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Token { get; set; } = "";   // Pay@ token, never the card number
    public string Brand { get; set; } = "";
    public string Last4 { get; set; } = "";
    public string Expiry { get; set; } = "";
}

public sealed class BillingDb(DbContextOptions<BillingDb> o) : DbContext(o)
{
    public DbSet<Charge> Charges => Set<Charge>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<SavedCard> Cards => Set<SavedCard>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Charge>().HasKey(x => x.PurchaseId);
        b.Entity<Payment>().HasKey(x => x.Id);
        b.Entity<Payment>().HasIndex(x => x.GatewayRef);
        b.Entity<Payment>().HasIndex(x => x.UserId);
        b.Entity<SavedCard>().HasKey(x => x.Id);
        b.Entity<SavedCard>().HasIndex(x => x.UserId);
    }
}

public sealed record PayIn(string PurchaseId, string Method, string? SavedCardId);
public sealed record PaymentOut(string Id, string PurchaseId, string Status, int AmountCents, string Description, string? CardFormUrl,
    string? ThreeDsUrl, string? Brand, string? Last4, string? DeclineReason);
/// <summary>MOCK ONLY. With real Pay@ the hosted form collects the card and this endpoint is disabled.</summary>
public sealed record CardIn(string Number, string Expiry, string Cvv, string Name, bool Save = false);
public sealed record OtpIn(string Otp);
public sealed record CardOut(string Id, string Brand, string Last4, string Expiry);

public static class Endpoints
{
    private static PaymentOut Out(Payment p, GatewayResult? g = null) => new(p.Id, p.PurchaseId, p.Status, p.AmountCents, p.Description,
        g?.CardFormUrl, g?.ThreeDsUrl, p.Brand, p.Last4, p.DeclineReason);

    private static async Task<Payment> Own(BillingDb db, string id, Principal u)
    {
        var p = await db.Payments.FindAsync(id);
        return p is null || p.UserId != u.UserId ? throw new NotFoundException("Payment not found", "payment_not_found") : p;
    }

    private static async Task Finish(BillingDb db, IEventBus bus, Payment p, GatewayResult g)
    {
        p.Status = g.Status;
        p.DeclineReason = g.DeclineReason;
        await db.SaveChangesAsync();
        if (g.Status == "succeeded")
            await bus.PublishAsync("billing.payment.succeeded", new { payment_id = p.Id, purchase_id = p.PurchaseId, user_id = p.UserId, amount_cents = p.AmountCents });
        else if (g.Status == "declined")
            await bus.PublishAsync("billing.payment.failed", new { payment_id = p.Id, purchase_id = p.PurchaseId, reason = g.DeclineReason });
    }

    public static void MapBilling(this WebApplication app)
    {
        var g = app.MapGroup("").WithTags("billing");

        g.MapPost("/payments", async (PayIn body, HttpContext ctx, BillingDb db, IGateway gateway, IClock clock) =>
        {
            var u = await ctx.RequireUserAsync();
            Check.Matches(body.Method, "^(new_card|saved_card)$", "method");
            var charge = await db.Charges.FindAsync(body.PurchaseId);
            if (charge is null || charge.UserId != u.UserId) throw new NotFoundException("Nothing to pay for", "charge_not_found");
            SavedCard? card = null;
            if (body.Method == "saved_card")
            {
                card = await db.Cards.FindAsync(body.SavedCardId ?? "");
                if (card is null || card.UserId != u.UserId) throw new NotFoundException("Saved card not found", "card_not_found");
            }
            var r = await gateway.CreateAsync(charge.PurchaseId, charge.AmountCents, card?.Token);
            var p = new Payment
            {
                Id = "PAY-" + Guid.NewGuid().ToString("N")[..10], PurchaseId = charge.PurchaseId, UserId = u.UserId, AmountCents = charge.AmountCents,
                Description = charge.Description, GatewayRef = r.GatewayRef, Status = r.Status, Brand = card?.Brand, Last4 = card?.Last4, CreatedAt = clock.UtcNow,
            };
            db.Payments.Add(p);
            await db.SaveChangesAsync();
            return Results.Json(Out(p, r), Json.Options, statusCode: 201);
        });

        g.MapPost("/payments/{id}/mock-card", async (string id, CardIn body, HttpContext ctx, BillingDb db, IGateway gateway) =>
        {
            if (!gateway.IsMock) throw new AppException("Card details go to Pay@'s secure form", "use_hosted_form");
            var u = await ctx.RequireUserAsync();
            Check.Matches(body.Number, @"^[\d ]{15,23}$", "number", "Check the card number");
            Check.Matches(body.Expiry, @"^(0[1-9]|1[0-2])/\d{2}$", "expiry", "Use MM/YY");
            Check.Matches(body.Cvv, @"^\d{3,4}$", "cvv");
            var p = await Own(db, id, u);
            var r = await gateway.SubmitCardAsync(p.GatewayRef, body.Number, body.Expiry, body.Cvv, body.Name, body.Save);
            (p.Status, p.DeclineReason) = (r.Status, r.DeclineReason);
            if (r.Last4 is not null) (p.Brand, p.Last4) = (r.Brand, r.Last4);
            if (r.CardToken is not null)
                db.Cards.Add(new SavedCard { Id = "CRD-" + Guid.NewGuid().ToString("N")[..8], UserId = u.UserId, Token = r.CardToken, Brand = r.Brand!, Last4 = r.Last4!, Expiry = body.Expiry });
            await db.SaveChangesAsync();
            return Out(p, r);
        });

        g.MapPost("/payments/{id}/mock-3ds", async (string id, OtpIn body, HttpContext ctx, BillingDb db, IGateway gateway, IEventBus bus) =>
        {
            if (!gateway.IsMock) throw new AppException("3-D Secure is completed on the bank's page", "use_bank_page");
            Check.Matches(body.Otp, @"^\d{6}$", "otp");
            var p = await Own(db, id, await ctx.RequireUserAsync());
            var r = await gateway.Confirm3dsAsync(p.GatewayRef, body.Otp);
            await Finish(db, bus, p, r);
            return Out(p, r);
        });

        // Pay@ tells us the final result (succeeded / declined). Idempotent.
        app.MapPost("/webhooks/payat", async (HttpContext ctx, BillingDb db, IGateway gateway, IEventBus bus) =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var raw = ms.ToArray();
            if (!gateway.VerifyWebhook(raw, ctx.Request.Headers["x-payat-signature"].ToString()))
                throw new UnauthorizedException("Bad signature", "bad_signature");
            var ev = JsonDocument.Parse(raw).RootElement;
            var gref = ev.GetProperty("gateway_ref").GetString();
            var p = await db.Payments.FirstOrDefaultAsync(x => x.GatewayRef == gref);
            if (p is not null && p.Status is not ("succeeded" or "declined"))
                await Finish(db, bus, p, new GatewayResult(gref!, ev.GetProperty("status").GetString()!,
                    DeclineReason: ev.TryGetProperty("reason", out var r) ? r.GetString() : null));
            return Results.NoContent();
        }).ExcludeFromDescription();

        g.MapGet("/payments/{id}", async (string id, HttpContext ctx, BillingDb db) => Out(await Own(db, id, await ctx.RequireUserAsync())));

        g.MapGet("/receipts", async (HttpContext ctx, BillingDb db) =>
        {
            var u = await ctx.RequireUserAsync();
            return (await db.Payments.Where(p => p.UserId == u.UserId && p.Status == "succeeded").ToListAsync())
                .OrderByDescending(p => p.CreatedAt).Select(p => Out(p));
        });

        g.MapGet("/cards", async (HttpContext ctx, BillingDb db) =>
        {
            var u = await ctx.RequireUserAsync();
            return (await db.Cards.Where(c => c.UserId == u.UserId).ToListAsync()).Select(c => new CardOut(c.Id, c.Brand, c.Last4, c.Expiry));
        });

        g.MapDelete("/cards/{id}", async (string id, HttpContext ctx, BillingDb db) =>
        {
            var u = await ctx.RequireUserAsync();
            await db.Cards.Where(c => c.Id == id && c.UserId == u.UserId).ExecuteDeleteAsync();
            return Results.NoContent();
        });
    }

    public static void Subscribe(WebApplication app)
    {
        var bus = app.Services.GetRequiredService<IEventBus>();
        bus.Subscribe("ticketing.purchase.created", async (_, ev) =>
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BillingDb>();
            var id = ev.GetProperty("purchase_id").GetString()!;
            if (await db.Charges.FindAsync(id) is not null) return;
            db.Charges.Add(new Charge { PurchaseId = id, UserId = ev.GetProperty("user_id").GetString()!,
                AmountCents = ev.GetProperty("amount_cents").GetInt32(), Description = ev.GetProperty("description").GetString()! });
            await db.SaveChangesAsync();
        });
        bus.Subscribe("identity.user.deleted", async (_, ev) =>
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BillingDb>();
            var uid = ev.GetProperty("user_id").GetString();
            await db.Cards.Where(c => c.UserId == uid).ExecuteDeleteAsync();
        });
    }
}

public static class BillingService
{
    public static WebApplicationBuilder CreateBuilder(string[] args, ServiceSettings? settings = null)
    {
        settings ??= new ServiceSettings("billing");
        var s = settings;
        var mode = s.Get("PAYAT_MODE") ?? "mock"; // mock | sandbox | live
        if (mode == "mock" && s.IsProduction) throw new InvalidOperationException("PAYAT_MODE=mock is not allowed in production");
        var b = ServiceHost.CreateBuilder(args, s);
        b.Services.AddServiceDb<BillingDb>(s);
        var secret = s.Get("PAYAT_WEBHOOK_SECRET") ?? "mock-secret";
        b.Services.AddSingleton<IGateway>(mode == "mock"
            ? new MockPayAt(secret)
            : new PayAtClient(s.Get("PAYAT_BASE_URL") ?? "", s.Get("PAYAT_MERCHANT_ID") ?? "", s.Get("PAYAT_API_KEY") ?? "", secret));
        return b;
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        var app = ServiceHost.Build<BillingDb>(builder);
        Endpoints.Subscribe(app);
        app.MapBilling();
        return app;
    }
}

public static class Program
{
    public static async Task Main(string[] args)
    {
        var app = BillingService.Build(BillingService.CreateBuilder(args));
        await ServiceHost.StartBusAsync(app);
        await app.RunAsync();
    }
}
