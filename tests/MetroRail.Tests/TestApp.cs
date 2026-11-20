using System.Net.Http.Json;
using System.Text.Json;
using MetroRail.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MetroRail.Tests;

/// <summary>Runs a service in-process: SQLite in memory, in-memory events, real HTTP pipeline.</summary>
public sealed class TestApp : IAsyncDisposable
{
    public WebApplication App { get; }
    public HttpClient Client { get; }
    public InMemoryEventBus Bus => App.Services.GetRequiredService<InMemoryEventBus>();

    private TestApp(WebApplication app)
    {
        App = app;
        Client = app.GetTestClient();
    }

    public static async Task<TestApp> StartAsync(
        Func<string[], ServiceSettings, WebApplicationBuilder> create,
        Func<WebApplicationBuilder, WebApplication> build,
        string service,
        Action<WebApplicationBuilder>? configure = null,
        Dictionary<string, string?>? env = null)
    {
        var vars = new Dictionary<string, string?>
        {
            ["ENVIRONMENT"] = "test", ["DATABASE_URL"] = "sqlite::memory:", ["EVENT_BUS"] = "memory",
            ["ALLOW_DEV_TOKENS"] = "true", ["JWKS_URL"] = null,
        };
        foreach (var (k, v) in env ?? []) vars[k] = v;
        var builder = create([], new ServiceSettings(service, vars));
        builder.WebHost.UseTestServer();
        configure?.Invoke(builder);
        var app = build(builder);
        await app.StartAsync();
        return new TestApp(app);
    }

    public T Get<T>() where T : notnull => App.Services.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
    }
}

public static class Http
{
    public static HttpRequestMessage Auth(this HttpRequestMessage m, string user = "u-test", string roles = "commuter")
    {
        m.Headers.Add("Authorization", $"Bearer dev:{user}:{roles}");
        return m;
    }

    public static async Task<(int Status, JsonElement Body)> Send(this HttpClient c, HttpMethod method, string path,
        object? body = null, string? user = null, string roles = "commuter")
    {
        var m = new HttpRequestMessage(method, path);
        if (body is not null) m.Content = JsonContent.Create(body, options: Json.Options);
        if (user is not null) m.Auth(user, roles);
        var r = await c.SendAsync(m);
        var text = await r.Content.ReadAsStringAsync();
        var json = string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        return ((int)r.StatusCode, json);
    }

    public static Task<(int Status, JsonElement Body)> Get(this HttpClient c, string path, string? user = null, string roles = "commuter") =>
        c.Send(HttpMethod.Get, path, null, user, roles);

    public static Task<(int Status, JsonElement Body)> Post(this HttpClient c, string path, object? body, string? user = null, string roles = "commuter") =>
        c.Send(HttpMethod.Post, path, body, user, roles);

    public static string Str(this JsonElement e, string name) => e.GetProperty(name).ValueKind == JsonValueKind.Null ? "" : e.GetProperty(name).ToString();
}
