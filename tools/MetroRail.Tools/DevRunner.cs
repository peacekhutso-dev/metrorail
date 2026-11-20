using System.Diagnostics;

namespace MetroRail.Tools;

/// <summary>
/// Runs every service locally WITHOUT Docker: SQLite files + a local NATS server, services on :8101-:8109,
/// and a small built-in gateway on :8080 that forwards /api/&lt;service&gt;/... exactly like Kong.
/// </summary>
internal static class DevRunner
{
    public static readonly string[] Services = ["network", "tracking", "eta", "realtime", "identity", "trips", "notifications", "ticketing", "billing"];
    private static readonly Dictionary<string, string> Projects = new()
    {
        ["network"] = "Network", ["tracking"] = "Tracking", ["eta"] = "Eta", ["realtime"] = "Realtime", ["identity"] = "Identity",
        ["trips"] = "Trips", ["notifications"] = "Notifications", ["ticketing"] = "Ticketing", ["billing"] = "Billing",
    };

    public static async Task<int> RunAsync(string[] args)
    {
        var root = Args.RepoRoot();
        var data = Path.Combine(root, ".dev-data");
        Directory.CreateDirectory(data);
        var ports = Services.Select((s, i) => (s, p: 8101 + i)).ToDictionary(x => x.s, x => x.p);
        var bus = Environment.GetEnvironmentVariable("EVENT_BUS") ?? "nats";
        var procs = new List<Process>();

        if (bus == "nats")
        {
            try { procs.Add(Process.Start(new ProcessStartInfo("nats-server", "-js -p 4222") { RedirectStandardOutput = true, RedirectStandardError = true })!); }
            catch { Console.Error.WriteLine("nats-server not found. Install it, or run with EVENT_BUS=memory"); return 1; }
            await Task.Delay(500);
        }

        foreach (var (name, port) in ports)
        {
            var dll = Path.Combine(root, "services", Projects[name], "bin", "Debug", "net10.0", $"MetroRail.{Projects[name]}.dll");
            if (!File.Exists(dll)) { Console.Error.WriteLine($"Build first: dotnet build ({dll} missing)"); return 1; }
            var psi = new ProcessStartInfo("dotnet", $"\"{dll}\"") { WorkingDirectory = root };
            psi.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
            psi.Environment["SERVICE_NAME"] = name;
            psi.Environment["ENVIRONMENT"] = "local";
            psi.Environment["EVENT_BUS"] = bus;
            psi.Environment["NATS_URL"] = "nats://127.0.0.1:4222";
            psi.Environment["DATABASE_URL"] = $"sqlite:{Path.Combine(data, name)}.db";
            psi.Environment["JWKS_URL"] = $"http://127.0.0.1:{ports["identity"]}/.well-known/jwks.json";
            psi.Environment["CENTRIFUGO_API_URL"] = "http://127.0.0.1:8001";
            psi.Environment["CENTRIFUGO_PUBLIC_URL"] = "http://localhost:8001";
            psi.Environment["Logging__LogLevel__Default"] = "Warning";
            foreach (var (s, p) in ports) psi.Environment[$"{s.ToUpperInvariant()}_URL"] = $"http://127.0.0.1:{p}";
            if (name == "identity") psi.Environment.Remove("JWKS_URL"); // identity verifies with its own key
            procs.Add(Process.Start(psi)!);
        }

        Console.WriteLine("Services: " + string.Join(", ", ports.Select(p => $"{p.Key}:{p.Value}")));
        Console.WriteLine("Gateway:  http://localhost:8080/api/<service>/...   (Ctrl+C to stop)");

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://0.0.0.0:8080");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        var gw = builder.Build();
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        gw.Map("/api/{service}/{**path}", async (string service, string? path, HttpContext ctx) =>
        {
            if (!ports.TryGetValue(service, out var port)) return Results.NotFound();
            var req = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), $"http://127.0.0.1:{port}/{path}{ctx.Request.QueryString}");
            if (ctx.Request.ContentLength > 0 || ctx.Request.Headers.TransferEncoding.Count > 0)
            {
                req.Content = new StreamContent(ctx.Request.Body);
                if (ctx.Request.ContentType is { } ct) req.Content.Headers.TryAddWithoutValidation("Content-Type", ct);
            }
            foreach (var h in ctx.Request.Headers.Where(h => h.Key is "Authorization" or "X-Request-Id" or "x-payat-signature"))
                req.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray());
            var r = await http.SendAsync(req);
            ctx.Response.StatusCode = (int)r.StatusCode;
            if (r.Content.Headers.ContentType is { } rct) ctx.Response.ContentType = rct.ToString();
            await r.Content.CopyToAsync(ctx.Response.Body);
            return Results.Empty;
        });
        gw.Lifetime.ApplicationStopping.Register(() =>
        {
            foreach (var p in procs) try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
        });
        await gw.RunAsync();
        return 0;
    }
}
