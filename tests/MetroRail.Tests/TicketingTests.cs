using System.Security.Cryptography;
using System.Text.Json;
using MetroRail.Ticketing;
using Microsoft.Extensions.DependencyInjection;

namespace MetroRail.Tests;

public class TicketingTests
{
    private static Task<TestApp> Start() => TestApp.StartAsync(TicketingService.CreateBuilder, TicketingService.Build, "ticketing");

    private static async Task<(JsonElement Ticket, ECDsa Key)> Buy(TestApp t, string product = "ten_trip", string user = "u1")
    {
        var (key, dpk) = TicketCrypto.DeviceKeyPair();
        var (status, p) = await t.Client.Post("/purchases", new { product_id = product, from_station_id = "ST-RONDEBOSCH", to_station_id = "ST-CLAREMONT", device_public_key = dpk }, user);
        Assert.Equal(201, status);
        Assert.Contains("ticketing.purchase.created", t.Bus.PublishedSubjects);
        await t.Bus.PublishAsync("billing.payment.succeeded", new { purchase_id = p.Str("id") });
        var (_, paid) = await t.Client.Get($"/purchases/{p.Str("id")}", user);
        Assert.Equal("paid", paid.Str("status"));
        return ((await t.Client.Get($"/tickets/{paid.Str("ticket_id")}", user)).Body, key);
    }

    private static string ScannerPub(TestApp t) =>
        JsonSerializer.SerializeToElement(t.Get<TicketCrypto.Keys>().Public()).GetProperty("scanner_x25519").GetString()!;

    private static async Task<(JsonElement Result, string Qr)> Scan(TestApp t, JsonElement ticket, ECDsa key, DateTimeOffset? at = null)
    {
        var qr = TicketCrypto.BuildQr(ticket.Str("cert"), key, ScannerPub(t), now: at);
        var (_, r) = await t.Client.Post("/validations", new { qr, train_no = "3412", scanner_id = "SC-000123" }, "staff-1", "conductor");
        return (r, qr);
    }

    [Theory]
    [InlineData(7, 0, 90)]     // short trip -> minimum 90 min
    [InlineData(60, 1, 120)]
    [InlineData(200, 2, 240)]  // capped at 4 h
    public void Journey_window(int scheduled, int changes, int expected) =>
        Assert.Equal(expected, Products.JourneyWindowMin(scheduled, changes));

    [Fact]
    public async Task Ten_trip_uses_one_trip_per_journey_not_per_scan()
    {
        await using var t = await Start();
        var (ticket, key) = await Buy(t);
        Assert.Equal(10, ticket.GetProperty("trips_total").GetInt32());
        Assert.Equal("ready", ticket.Str("status"));
        var (v1, _) = await Scan(t, ticket, key);
        Assert.Equal("valid", v1.Str("result"));
        Assert.True(v1.GetProperty("new_trip_started").GetBoolean());
        Assert.Equal(1, v1.GetProperty("trip_no").GetInt32());
        var (v2, _) = await Scan(t, ticket, key); // second conductor, same journey
        Assert.Equal("valid", v2.Str("result"));
        Assert.False(v2.GetProperty("new_trip_started").GetBoolean());
        Assert.Equal(9, v2.GetProperty("trips_left").GetInt32());
        var (_, mine) = await t.Client.Get($"/tickets/{ticket.Str("id")}", "u1");
        Assert.Equal("in_progress", mine.Str("status"));
    }

    [Fact]
    public async Task Same_code_twice_is_rejected()
    {
        await using var t = await Start();
        var (ticket, key) = await Buy(t);
        var (_, qr) = await Scan(t, ticket, key);
        var (_, r) = await t.Client.Post("/validations", new { qr, train_no = "3412", scanner_id = "SC-000124" }, "staff-2", "conductor");
        Assert.Equal("rejected", r.Str("result"));
        Assert.Equal("already_scanned", r.Str("reason"));
    }

    [Fact]
    public async Task Old_code_photo_is_rejected()
    {
        await using var t = await Start();
        var (ticket, key) = await Buy(t);
        Assert.Equal("expired_code", (await Scan(t, ticket, key, DateTimeOffset.UtcNow.AddMinutes(-2))).Result.Str("reason"));
    }

    [Fact]
    public async Task Ticket_on_another_phone_is_rejected()
    {
        await using var t = await Start();
        var (ticket, _) = await Buy(t);
        var (otherPhone, _) = TicketCrypto.DeviceKeyPair();
        Assert.Equal("not_this_phone", (await Scan(t, ticket, otherPhone)).Result.Str("reason"));
    }

    [Theory]
    [InlineData("https://example.com", "not_a_metrorail_code")]
    [InlineData("MR1:AAAA", "invalid_code")]
    public async Task Garbage_and_non_metrorail_codes(string qr, string reason)
    {
        await using var t = await Start();
        var (_, r) = await t.Client.Post("/validations", new { qr, train_no = "3412", scanner_id = "SC-000123" }, "staff-1", "conductor");
        Assert.Equal(reason, r.Str("reason"));
    }

    [Fact]
    public async Task Single_trip_used_up_then_no_trips_left()
    {
        await using var t = await Start();
        var (ticket, key) = await Buy(t, "single");
        Assert.Equal("valid", (await Scan(t, ticket, key)).Result.Str("result"));
        using (var scope = t.App.Services.CreateScope()) // close the journey window = a later journey
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketingDb>();
            (await db.Tickets.FindAsync(ticket.Str("id")))!.OpenTripUntil = null;
            await db.SaveChangesAsync();
        }
        Assert.Equal("no_trips_left", (await Scan(t, ticket, key)).Result.Str("reason"));
    }

    [Fact]
    public async Task Topup_adds_trips_and_reissues_cert()
    {
        await using var t = await Start();
        var (ticket, key) = await Buy(t);
        var (_, p) = await t.Client.Post($"/tickets/{ticket.Str("id")}/topup", new { trips = 10 }, "u1");
        Assert.Equal(9500, p.GetProperty("amount_cents").GetInt32());
        await t.Bus.PublishAsync("billing.payment.succeeded", new { purchase_id = p.Str("id") });
        var (_, t2) = await t.Client.Get($"/tickets/{ticket.Str("id")}", "u1");
        Assert.Equal(20, t2.GetProperty("trips_total").GetInt32());
        Assert.NotEqual(ticket.Str("cert"), t2.Str("cert"));
        Assert.Equal("replaced_ticket", (await Scan(t, ticket, key)).Result.Str("reason")); // old certificate
        Assert.Equal("valid", (await Scan(t, t2, key)).Result.Str("result"));
    }

    [Fact]
    public async Task Only_conductors_can_validate()
    {
        await using var t = await Start();
        var (status, _) = await t.Client.Post("/validations", new { qr = "MR1:x", train_no = "3412", scanner_id = "SC-000123" }, "u1");
        Assert.Equal(403, status);
    }
}
