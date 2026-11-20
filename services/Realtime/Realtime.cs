using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MetroRail.Common;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MetroRail.Realtime;

/// <summary>Centrifugo server API (v5). Channels: trains:&lt;train_no&gt; (public), alerts:#&lt;user_id&gt; (private).</summary>
public interface ICentrifugo
{
    Task PublishAsync(string channel, object data);
}

public sealed class Centrifugo(string apiUrl, string apiKey) : ICentrifugo
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri(apiUrl), Timeout = TimeSpan.FromSeconds(3) };

    public async Task PublishAsync(string channel, object data)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/publish") { Content = JsonContent.Create(new { channel, data }, options: Json.Options) };
        req.Headers.Add("X-API-Key", apiKey);
        (await _http.SendAsync(req)).EnsureSuccessStatusCode();
    }
}

public sealed class FakeCentrifugo : ICentrifugo
{
    public List<(string Channel, JsonElement Data)> Sent { get; } = [];

    public Task PublishAsync(string channel, object data)
    {
        Sent.Add((channel, JsonSerializer.SerializeToElement(data, Json.Options)));
        return Task.CompletedTask;
    }
}

public static class Endpoints
{
    private const int MaxTrains = 5;
    private static readonly string[] TrainFields =
        ["train_no", "lat", "lng", "state", "stationary_s", "last_station_id", "next_station_id", "at_station_id", "delay_min", "seen_at"];

    public static void MapRealtime(this WebApplication app)
    {
        // Token for Centrifugo's SSE endpoint. Subscriptions are decided here (server-side), so the phone only
        // needs a plain EventSource: GET /connection/uni_sse?cf_connect={"token": ...}
        app.MapGet("/token", async (string? trains, HttpContext ctx, ServiceSettings s) =>
        {
            var nums = (trains ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Take(MaxTrains).ToList();
            if (nums.Any(t => !t.All(char.IsDigit))) throw new AppException("Train numbers only", "invalid_trains");
            var channels = nums.Select(t => $"trains:{t}").ToList();
            var user = await ctx.OptionalUserAsync();
            if (user is not null) channels.Add($"alerts:#{user.UserId}");
            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Claims = new Dictionary<string, object> { ["sub"] = user?.UserId ?? "", ["channels"] = channels.ToArray() },
                Expires = Clock.RealUtcNow.UtcDateTime.AddHours(1),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret(s))), SecurityAlgorithms.HmacSha256),
            });
            return new { token, sse_url = (s.Get("CENTRIFUGO_PUBLIC_URL") ?? "http://localhost:8001") + "/connection/uni_sse", channels };
        }).WithTags("realtime");
    }

    public static string Secret(ServiceSettings s) => s.Get("CENTRIFUGO_TOKEN_SECRET") ?? "dev-token-secret-change-me-32-bytes!!";

    public static void Subscribe(WebApplication app)
    {
        var bus = app.Services.GetRequiredService<IEventBus>();
        var centrifugo = app.Services.GetRequiredService<ICentrifugo>();
        var log = app.Services.GetRequiredService<ILogger<Centrifugo>>();

        async Task Forward(string subject, JsonElement ev)
        {
            try
            {
                if (subject.StartsWith("tracking.train."))
                {
                    var data = new Dictionary<string, object?> { ["type"] = subject[(subject.LastIndexOf('.') + 1)..] };
                    foreach (var f in TrainFields) data[f] = ev.TryGetProperty(f, out var v) ? v : null;
                    await centrifugo.PublishAsync($"trains:{ev.GetProperty("train_no").GetString()}", data);
                }
                else if (subject == "notifications.alert.created")
                {
                    await centrifugo.PublishAsync($"alerts:#{ev.GetProperty("user_id").GetString()}", new
                    {
                        type = "alert", id = ev.GetProperty("id").GetString(), kind = ev.GetProperty("kind").GetString(),
                        title = ev.GetProperty("title").GetString(), body = ev.GetProperty("body").GetString(),
                        train_no = ev.TryGetProperty("train_no", out var t) ? t.GetString() : null,
                    });
                }
            }
            catch (Exception e) // live updates are best-effort; never crash the consumer
            {
                log.LogWarning("centrifugo_publish_failed {Subject}: {Error}", subject, e.Message);
            }
        }

        foreach (var subject in new[] { "tracking.train.position", "tracking.train.state_changed", "notifications.alert.created" })
            bus.Subscribe(subject, (s, ev) => Forward(s, ev));
    }
}

public static class RealtimeService
{
    public static WebApplicationBuilder CreateBuilder(string[] args, ServiceSettings? settings = null)
    {
        settings ??= new ServiceSettings("realtime");
        var s = settings;
        var b = ServiceHost.CreateBuilder(args, s);
        b.Services.AddSingleton<ICentrifugo>(new Centrifugo(s.Get("CENTRIFUGO_API_URL") ?? "http://centrifugo:8000", s.Get("CENTRIFUGO_API_KEY") ?? "dev-api-key"));
        return b;
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        var app = ServiceHost.Build<NoDb>(builder);
        Endpoints.Subscribe(app);
        app.MapRealtime();
        return app;
    }
}

public static class Program
{
    public static async Task Main(string[] args)
    {
        var app = RealtimeService.Build(RealtimeService.CreateBuilder(args));
        await ServiceHost.StartBusAsync(app);
        await app.RunAsync();
    }
}
