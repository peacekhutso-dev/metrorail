using System.Net.Http.Json;
using System.Text.Json;
using MetroRail.Ticketing;

namespace MetroRail.Tools;

/// <summary>
/// End-to-end smoke test against a running stack (docker-compose or `dev`), through the gateway:
/// log in with a code, save a trip, train 3412 runs late then stops, ETA moves, an alert arrives, buy a
/// 10-trip ticket by card, a conductor scans the rotating QR (and a re-used code is refused).
/// </summary>
internal static class Smoke
{
    private static void Ok(string msg) => Console.WriteLine($"  ✓ {msg}");
    private static void Assert(bool ok, string what) { if (!ok) throw new Exception(what); }

    public static async Task<int> RunAsync(string[] args)
    {
        var api = Args.Get(args, "--api", Environment.GetEnvironmentVariable("API") ?? "http://localhost:8080/api").TrimEnd('/') + "/";
        using var c = new HttpClient { BaseAddress = new Uri(api), Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            Console.WriteLine("Login");
            var start = await Post(c, "identity/auth/start", new { phone = "082 456 4567" });
            var tok = await Post(c, "identity/auth/verify", new { challenge_id = S(start, "challenge_id"), code = S(start, "dev_code") });
            var me = "Bearer " + S(tok, "access_token");
            Ok($"code sent to {S(start, "sent_to")}, user {S(tok, "user_id")} (real ES256 token)");
            await Get(c, "identity/me", me);
            Ok("token accepted by identity");

            Console.WriteLine("Saved trip");
            var trip = await Post(c, "trips/trips", new
            {
                name = "Work", from_station_id = "ST-RONDEBOSCH", to_station_id = "ST-CLAREMONT",
                days = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" }, usual_time = "07:18", train_no = "3412",
            }, me);
            Ok($"{S(trip, "name")}: Rondebosch → Claremont, train {S(trip, "train_no")} (token verified via JWKS by trips)");
            await Task.Delay(500); // notifications receives trips.trip.saved over NATS

            Console.WriteLine("Train 3412 live");
            const string feed = "Bearer dev:simulator:feed";
            var st = (await Get(c, "network/stations")).EnumerateArray().ToDictionary(s => S(s, "id"), s => (Lat: s.GetProperty("lat").GetDouble(), Lng: s.GetProperty("lng").GetDouble()));
            var mid = new { lat = (st["ST-MOWBRAY"].Lat + st["ST-ROSEBANK"].Lat) / 2, lng = (st["ST-MOWBRAY"].Lng + st["ST-ROSEBANK"].Lng) / 2 };
            var r = await Post(c, "tracking/positions", new { train_no = "3412", lat = st["ST-MOWBRAY"].Lat, lng = st["ST-MOWBRAY"].Lng, source = "simulator" }, feed);
            Ok($"at Mowbray: state={S(r, "state")} delay={S(r, "delay_min")} min");
            r = await Post(c, "tracking/positions", new { train_no = "3412", mid.lat, mid.lng, source = "simulator" }, feed);
            var j = await Get(c, "eta/trains/3412/journey?board=ST-RONDEBOSCH&alight=ST-CLAREMONT");
            Ok($"moving: reaches Rondebosch {S(j, "board_predicted")} (sched {S(j, "board_scheduled")}), Claremont {S(j, "alight_predicted")}, {S(j, "stops_until_board")} stops away");
            var movingEta = S(j, "board_predicted");

            var seen = r.GetProperty("seen_at").GetDateTimeOffset();
            r = await Post(c, "tracking/positions", new { train_no = "3412", mid.lat, mid.lng, source = "simulator", at = seen.AddMinutes(6) }, feed);
            Assert(S(r, "state") == "stopped", $"expected stopped, got {r}");
            Ok($"6 min without moving -> state={S(r, "state")}");
            await Task.Delay(500);
            var alerts = await Get(c, "notifications/alerts", me);
            Assert(alerts.GetArrayLength() > 0 && S(alerts[0], "kind") == "stopped", $"expected a 'stopped' alert, got {alerts}");
            Ok($"alert: '{S(alerts[0], "title")}' — {S(alerts[0], "body")}");
            j = await Get(c, "eta/trains/3412/journey?board=ST-RONDEBOSCH&alight=ST-CLAREMONT");
            Ok($"stopped: ETA moved {movingEta} -> {S(j, "board_predicted")} (likely {S(j, "likely_from")}–{S(j, "likely_to")})");

            Console.WriteLine("Buy a 10-trip ticket by card");
            var (key, dpk) = TicketCrypto.DeviceKeyPair();
            var p = await Post(c, "ticketing/purchases", new { product_id = "ten_trip", from_station_id = "ST-RONDEBOSCH", to_station_id = "ST-CLAREMONT", device_public_key = dpk }, me);
            Ok($"{S(p, "description")} R{p.GetProperty("amount_cents").GetInt32() / 100.0:0.00}");
            await Task.Delay(500); // billing learns the amount from ticketing.purchase.created
            var pay = await Post(c, "billing/payments", new { purchase_id = S(p, "id"), method = "new_card" }, me);
            pay = await Post(c, $"billing/payments/{S(pay, "id")}/mock-card", new { number = "4000 0000 0000 0002", expiry = "08/29", cvv = "123", name = "P Molimo", save = true }, me);
            Ok($"card {S(pay, "brand")} •••• {S(pay, "last4")} -> {S(pay, "status")}");
            pay = await Post(c, $"billing/payments/{S(pay, "id")}/mock-3ds", new { otp = "123456" }, me);
            Ok($"3-D Secure -> {S(pay, "status")}");
            await Task.Delay(500);
            p = await Get(c, $"ticketing/purchases/{S(p, "id")}", me);
            var t = await Get(c, $"ticketing/tickets/{S(p, "ticket_id")}", me);
            Ok($"ticket {S(t, "id")} issued: {S(t, "trips_left")} of {S(t, "trips_total")} trips");

            Console.WriteLine("Conductor scans");
            const string conductor = "Bearer dev:staff-1:conductor";
            var scannerPub = S(await Get(c, "ticketing/keys"), "scanner_x25519");
            var qr = TicketCrypto.BuildQr(S(t, "cert"), key, scannerPub);
            var v = await Post(c, "ticketing/validations", new { qr, train_no = "3412", scanner_id = "SC-000123" }, conductor);
            Assert(S(v, "result") == "valid", v.ToString());
            Ok($"VALID trip {S(v, "trip_no")} of 10, {S(v, "trips_left")} left");
            v = await Post(c, "ticketing/validations", new { qr, train_no = "3412", scanner_id = "SC-000124" }, conductor);
            Assert(S(v, "reason") == "already_scanned", v.ToString());
            Ok("same code again -> REJECTED (already_scanned)");
            v = await Post(c, "ticketing/validations", new { qr = TicketCrypto.BuildQr(S(t, "cert"), key, scannerPub), train_no = "3412", scanner_id = "SC-000124" }, conductor);
            Assert(S(v, "result") == "valid" && !v.GetProperty("new_trip_started").GetBoolean(), v.ToString());
            Ok("second conductor, fresh code, same journey -> VALID, no extra trip used");

            Console.WriteLine("POPIA");
            var exp = await Get(c, "identity/me/export", me);
            Ok("export: " + string.Join(", ", exp.GetProperty("account").EnumerateObject().Select(x => x.Name)));
            Console.WriteLine("\nAll good.");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("FAILED: " + e.Message);
            return 1;
        }
    }

    private static string S(JsonElement e, string p) => e.GetProperty(p).ToString();

    private static async Task<JsonElement> Get(HttpClient c, string path, string? auth = null)
    {
        var m = new HttpRequestMessage(HttpMethod.Get, path);
        if (auth is not null) m.Headers.Add("Authorization", auth);
        return await Read(await c.SendAsync(m), path);
    }

    private static async Task<JsonElement> Post(HttpClient c, string path, object body, string? auth = null)
    {
        var m = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (auth is not null) m.Headers.Add("Authorization", auth);
        return await Read(await c.SendAsync(m), path);
    }

    private static async Task<JsonElement> Read(HttpResponseMessage r, string path)
    {
        var text = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode) throw new Exception($"{path} -> {(int)r.StatusCode} {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
