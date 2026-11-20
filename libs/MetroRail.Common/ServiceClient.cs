using System.Net.Http.Json;
using System.Text.Json;

namespace MetroRail.Common;

/// <summary>
/// Service-to-service HTTP (internal network, not through the gateway). Base URLs from env:
/// NETWORK_URL, TRACKING_URL, ... (defaults to docker-compose names). Tests swap in fakes.
/// </summary>
public interface IServiceClient
{
    Task<JsonElement> GetAsync(string path);
}

public sealed class ServiceClient : IServiceClient
{
    private static readonly Dictionary<string, string> Defaults = new()
    {
        ["network"] = "http://network:8000", ["tracking"] = "http://tracking:8000", ["eta"] = "http://eta:8000",
        ["trips"] = "http://trips:8000", ["ticketing"] = "http://ticketing:8000", ["billing"] = "http://billing:8000",
        ["identity"] = "http://identity:8000",
    };

    private readonly HttpClient _http;
    private readonly string _name;

    public ServiceClient(string name, string? baseUrl = null)
    {
        _name = name;
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl ?? Environment.GetEnvironmentVariable($"{name.ToUpperInvariant()}_URL") ?? Defaults[name]),
            Timeout = TimeSpan.FromSeconds(3),
        };
    }

    public async Task<JsonElement> GetAsync(string path)
    {
        using var r = await _http.GetAsync(path.TrimStart('/'));
        if ((int)r.StatusCode >= 400)
        {
            try
            {
                var body = await r.Content.ReadFromJsonAsync<JsonElement>();
                var err = body.GetProperty("error");
                throw new AppException(err.GetProperty("message").GetString()!, err.GetProperty("code").GetString()!, (int)r.StatusCode);
            }
            catch (Exception e) when (e is not AppException)
            {
                throw new AppException($"{_name} returned {(int)r.StatusCode}", "upstream_error", 502);
            }
        }
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }
}
