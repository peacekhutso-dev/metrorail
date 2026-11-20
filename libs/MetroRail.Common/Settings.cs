namespace MetroRail.Common;

/// <summary>
/// Settings shared by all services, read from environment variables (12-factor):
/// SERVICE_NAME, ENVIRONMENT, DATABASE_URL, EVENT_BUS, NATS_URL, JWKS_URL, JWT_PUBLIC_KEY,
/// ALLOW_DEV_TOKENS, CORS_ORIGINS. Service-specific values are read with <see cref="Get"/>.
/// </summary>
public sealed class ServiceSettings
{
    private readonly Dictionary<string, string?> _overrides;

    public ServiceSettings(string serviceName, IDictionary<string, string?>? overrides = null)
    {
        _overrides = overrides is null ? new() : new(overrides, StringComparer.OrdinalIgnoreCase);
        ServiceName = Get("SERVICE_NAME") ?? serviceName;
        Environment = Get("ENVIRONMENT") ?? "local";
        DatabaseUrl = Get("DATABASE_URL") ?? "sqlite::memory:";
        EventBus = Get("EVENT_BUS") ?? "memory";
        NatsUrl = Get("NATS_URL") ?? "nats://localhost:4222";
        JwtIssuer = Get("JWT_ISSUER") ?? "metrorail-identity";
        JwtAudience = Get("JWT_AUDIENCE") ?? "metrorail";
        JwksUrl = Get("JWKS_URL");
        JwtPublicKeyPem = Get("JWT_PUBLIC_KEY");
        AllowDevTokens = (Get("ALLOW_DEV_TOKENS") ?? "true").Equals("true", StringComparison.OrdinalIgnoreCase);
        CorsOrigins = (Get("CORS_ORIGINS") ?? "http://localhost:5173,capacitor://localhost,http://localhost")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public string ServiceName { get; }
    /// <summary>local | test | staging | production</summary>
    public string Environment { get; }
    /// <summary>postgresql://user:pass@host:5432/db, a Npgsql connection string, or sqlite::memory: / sqlite:path.db</summary>
    public string DatabaseUrl { get; }
    /// <summary>memory (tests, single process) | nats (compose / production)</summary>
    public string EventBus { get; }
    public string NatsUrl { get; }
    public string JwtIssuer { get; }
    public string JwtAudience { get; }
    public string? JwksUrl { get; }
    public string? JwtPublicKeyPem { get; set; }
    /// <summary>Local/test only: accept "Bearer dev:&lt;user&gt;:&lt;roles&gt;". Never in production.</summary>
    public bool AllowDevTokens { get; }
    public string[] CorsOrigins { get; }

    public bool IsProduction => Environment == "production";

    public string? Get(string key) =>
        _overrides.TryGetValue(key, out var v) ? v : System.Environment.GetEnvironmentVariable(key);
}
