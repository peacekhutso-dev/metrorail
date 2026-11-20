using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using MetroRail.Ticketing;

namespace MetroRail.Tools;

/// <summary>Checks that a QR built by the customer app (TypeScript, apps/customer/src/ticket/qr.ts) is
/// accepted by the ticketing service (C#), and that the same ticket from another phone is refused.</summary>
internal static class QrInterop
{
    public static async Task<int> RunAsync(string[] args)
    {
        var app = Path.Combine(Args.RepoRoot(), "apps", "customer");
        var keys = TicketCrypto.Keys.LoadOrGenerate(null, null);
        var scannerPub = JsonSerializer.SerializeToElement(keys.Public()).GetProperty("scanner_x25519").GetString()!;

        var phone = await Tsx(app);
        var cert = TicketCrypto.IssueCert(keys.Issuer, new JsonObject
        {
            ["v"] = 1, ["tid"] = "MR-48213", ["uid"] = "u1", ["prd"] = "ten_trip", ["from"] = "ST-RONDEBOSCH", ["to"] = "ST-CLAREMONT",
            ["line"] = "LN-SOUTHERN", ["trips"] = 10, ["vf"] = 0, ["vt"] = 4102444800, ["dpk"] = phone.GetProperty("dpk").GetString(),
        });
        var qr = (await Tsx(app, cert, scannerPub, phone.GetProperty("pkcs8").GetString()!)).GetProperty("qr").GetString()!;
        var scanned = TicketCrypto.ReadQr(keys, qr);
        if (scanned.Cert["tid"]!.GetValue<string>() != "MR-48213") { Console.WriteLine("✗ wrong ticket id"); return 1; }
        Console.WriteLine($"✓ app QR ({qr.Length} chars) opened and verified by ticketing (C#): {scanned.Cert["tid"]}");

        var other = await Tsx(app);
        try
        {
            TicketCrypto.ReadQr(keys, (await Tsx(app, cert, scannerPub, other.GetProperty("pkcs8").GetString()!)).GetProperty("qr").GetString()!);
            Console.WriteLine("✗ QR from another phone was accepted");
            return 1;
        }
        catch (TicketException e) when (e.Reason == "not_this_phone")
        {
            Console.WriteLine("✓ same ticket signed by another phone -> rejected (not_this_phone)");
        }
        return 0;
    }

    private static async Task<JsonElement> Tsx(string cwd, params string[] args)
    {
        var psi = new ProcessStartInfo("npx", ["tsx", "scripts/qr-interop.ts", .. args])
            { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new Exception("tsx failed: " + await p.StandardError.ReadToEndAsync());
        return JsonDocument.Parse(output.Trim().Split('\n')[^1]).RootElement.Clone();
    }
}
