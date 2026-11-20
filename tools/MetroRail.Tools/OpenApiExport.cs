using System.Text.Json;
using System.Text.Json.Nodes;
using MetroRail.Common;
using Microsoft.AspNetCore.TestHost;

namespace MetroRail.Tools;

/// <summary>Writes contracts/openapi/&lt;service&gt;.json from the services' own code (single source of truth).
/// CI regenerates and fails if the committed files differ.</summary>
internal static class OpenApiExport
{
    private static readonly (string Name, Func<string[], ServiceSettings, WebApplicationBuilder> Create, Func<WebApplicationBuilder, WebApplication> Build)[] Apps =
    [
        ("network", (a, s) => Network.NetworkService.CreateBuilder(a, s), Network.NetworkService.Build),
        ("tracking", (a, s) => Tracking.TrackingService.CreateBuilder(a, s), Tracking.TrackingService.Build),
        ("eta", (a, s) => Eta.EtaService.CreateBuilder(a, s), Eta.EtaService.Build),
        ("realtime", (a, s) => Realtime.RealtimeService.CreateBuilder(a, s), Realtime.RealtimeService.Build),
        ("identity", (a, s) => Identity.IdentityService.CreateBuilder(a, s), Identity.IdentityService.Build),
        ("trips", (a, s) => Trips.TripsService.CreateBuilder(a, s), Trips.TripsService.Build),
        ("notifications", (a, s) => Notifications.NotificationsService.CreateBuilder(a, s), Notifications.NotificationsService.Build),
        ("ticketing", (a, s) => Ticketing.TicketingService.CreateBuilder(a, s), Ticketing.TicketingService.Build),
        ("billing", (a, s) => Billing.BillingService.CreateBuilder(a, s), Billing.BillingService.Build),
    ];

    public static async Task<int> RunAsync(string[] args)
    {
        var outDir = Path.Combine(Args.RepoRoot(), "contracts", "openapi");
        Directory.CreateDirectory(outDir);
        foreach (var (name, create, build) in Apps)
        {
            var b = create([], new ServiceSettings(name, new Dictionary<string, string?>
                { ["ENVIRONMENT"] = "test", ["DATABASE_URL"] = "sqlite::memory:", ["EVENT_BUS"] = "memory" }));
            b.WebHost.UseTestServer();
            b.Logging.ClearProviders();
            await using var app = build(b);
            await app.StartAsync();
            var spec = JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi.json"))!.AsObject();
            spec["servers"] = new JsonArray(new JsonObject { ["url"] = $"/api/{name}", ["description"] = "via the gateway" });
            var path = Path.Combine(outDir, $"{name}.json");
            await File.WriteAllTextAsync(path, spec.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Console.WriteLine($"contracts/openapi/{name}.json  ({spec["paths"]!.AsObject().Count} paths)");
            await app.StopAsync();
        }
        return 0;
    }
}
