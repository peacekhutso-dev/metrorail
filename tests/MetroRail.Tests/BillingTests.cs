using System.Security.Cryptography;
using System.Text;
using MetroRail.Billing;
using Microsoft.Extensions.DependencyInjection;

namespace MetroRail.Tests;

public class BillingTests
{
    private static readonly object Good = new { number = "4000 0000 0000 0002", expiry = "08/29", cvv = "123", name = "P Molimo", save = true };

    private static Task<TestApp> Start() => TestApp.StartAsync(BillingService.CreateBuilder, BillingService.Build, "billing");

    private static async Task<System.Text.Json.JsonElement> Setup(TestApp t, string purchase = "PUR-1")
    {
        await t.Bus.PublishAsync("ticketing.purchase.created", new { purchase_id = purchase, user_id = "u1", amount_cents = 1200, description = "Single trip" });
        var (status, p) = await t.Client.Post("/payments", new { purchase_id = purchase, method = "new_card" }, "u1");
        Assert.Equal(201, status);
        return p;
    }

    [Fact]
    public void Luhn() => Assert.True(Cards.LuhnOk("4000000000000002") && !Cards.LuhnOk("4000000000000001"));

    [Fact]
    public async Task New_card_3ds_success_saves_card_and_emits_event()
    {
        await using var t = await Start();
        var p = await Setup(t);
        Assert.Equal("requires_card", p.Str("status"));
        Assert.Equal(1200, p.GetProperty("amount_cents").GetInt32());
        var (_, r) = await t.Client.Post($"/payments/{p.Str("id")}/mock-card", Good, "u1");
        Assert.Equal("requires_3ds", r.Str("status"));
        Assert.Equal("0002", r.Str("last4"));
        Assert.Equal("visa", r.Str("brand"));
        Assert.Equal("succeeded", (await t.Client.Post($"/payments/{p.Str("id")}/mock-3ds", new { otp = "123456" }, "u1")).Body.Str("status"));
        Assert.Contains("billing.payment.succeeded", t.Bus.PublishedSubjects);
        var (_, cards) = await t.Client.Get("/cards", "u1");
        Assert.Equal("0002", cards[0].Str("last4"));
        // saved card -> next payment goes straight to bank approval
        await t.Bus.PublishAsync("ticketing.purchase.created", new { purchase_id = "PUR-2", user_id = "u1", amount_cents = 1200, description = "Single trip" });
        var (_, second) = await t.Client.Post("/payments", new { purchase_id = "PUR-2", method = "saved_card", saved_card_id = cards[0].Str("id") }, "u1");
        Assert.Equal("requires_3ds", second.Str("status"));
    }

    [Fact]
    public async Task Declined_and_bad_card()
    {
        await using var t = await Start();
        var p = await Setup(t);
        var (_, bad) = await t.Client.Post($"/payments/{p.Str("id")}/mock-card", new { number = "4000 0000 0000 0001", expiry = "08/29", cvv = "123", name = "P" }, "u1");
        Assert.Equal("invalid_card_number", bad.Str("decline_reason"));
        await t.Client.Post($"/payments/{p.Str("id")}/mock-card", new { number = "4000 0000 0000 0069", expiry = "08/29", cvv = "123", name = "P" }, "u1");
        var (_, r) = await t.Client.Post($"/payments/{p.Str("id")}/mock-3ds", new { otp = "123456" }, "u1");
        Assert.Equal("declined", r.Str("status"));
        Assert.Equal("declined_by_bank", r.Str("decline_reason"));
        Assert.Contains("billing.payment.failed", t.Bus.PublishedSubjects);
    }

    [Fact]
    public async Task Cannot_pay_someone_elses_purchase()
    {
        await using var t = await Start();
        await t.Bus.PublishAsync("ticketing.purchase.created", new { purchase_id = "PUR-1", user_id = "u1", amount_cents = 1200, description = "Single trip" });
        Assert.Equal(404, (await t.Client.Post("/payments", new { purchase_id = "PUR-1", method = "new_card" }, "u2")).Status);
    }

    [Fact]
    public async Task Webhook_signature_and_idempotency()
    {
        await using var t = await Start();
        var p = await Setup(t);
        string gref;
        using (var scope = t.App.Services.CreateScope())
            gref = (await scope.ServiceProvider.GetRequiredService<BillingDb>().Payments.FindAsync(p.Str("id")))!.GatewayRef;
        var body = Encoding.UTF8.GetBytes($"{{\"gateway_ref\":\"{gref}\",\"status\":\"succeeded\"}}");
        var sig = Convert.ToHexString(HMACSHA256.HashData("mock-secret"u8.ToArray(), body)).ToLowerInvariant();

        async Task<int> Hook(string s)
        {
            var m = new HttpRequestMessage(HttpMethod.Post, "/webhooks/payat") { Content = new ByteArrayContent(body) };
            m.Headers.Add("x-payat-signature", s);
            return (int)(await t.Client.SendAsync(m)).StatusCode;
        }

        Assert.Equal(401, await Hook("bad"));
        Assert.Equal(204, await Hook(sig));
        Assert.Equal(204, await Hook(sig));
        Assert.Single(t.Bus.PublishedSubjects, s => s == "billing.payment.succeeded");
    }
}
