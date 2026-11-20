using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MetroRail.Common;

public sealed record Principal(string UserId, IReadOnlySet<string> Roles)
{
    public void Require(string role)
    {
        if (!Roles.Contains(role)) throw new ForbiddenException($"Requires role '{role}'", "missing_role");
    }
}

/// <summary>
/// Verifies access tokens: short-lived JWTs signed by the identity service with ES256 (ECDSA P-256).
/// Keys come from JWKS_URL (cached) or JWT_PUBLIC_KEY (PEM). In local/test, "Bearer dev:&lt;user&gt;:&lt;roles&gt;"
/// is also accepted so tools and tests can act as any user or role.
/// </summary>
public sealed class TokenVerifier(ServiceSettings settings, IHttpClientFactory http)
{
    private readonly JsonWebTokenHandler _handler = new();
    private IList<SecurityKey>? _keys;
    private DateTimeOffset _keysAt;

    public async Task<Principal> VerifyAsync(string token)
    {
        if (settings.AllowDevTokens && !settings.IsProduction && token.StartsWith("dev:"))
        {
            var parts = token[4..].Split(':');
            var roles = parts.Length > 1 && parts[1].Length > 0 ? parts[1].Split(',') : ["commuter"];
            return new Principal(parts[0], roles.ToHashSet());
        }
        var result = await _handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = settings.JwtIssuer,
            ValidAudience = settings.JwtAudience,
            IssuerSigningKeys = await KeysAsync(),
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
        });
        if (!result.IsValid)
            throw new UnauthorizedException($"Invalid token: {result.Exception?.Message}", "invalid_token");
        var sub = result.Claims["sub"]?.ToString() ?? throw new UnauthorizedException("Token has no subject", "invalid_token");
        var roleSet = new HashSet<string>();
        if (result.Claims.TryGetValue("roles", out var r))
        {
            if (r is IEnumerable<object> list) foreach (var x in list) roleSet.Add(x.ToString()!);
            else if (r is string s) roleSet.Add(s);
        }
        return new Principal(sub, roleSet);
    }

    private async Task<IList<SecurityKey>> KeysAsync()
    {
        if (settings.JwksUrl is { } url)
        {
            if (_keys is null || Clock.RealUtcNow - _keysAt > TimeSpan.FromMinutes(10))
            {
                var json = await http.CreateClient().GetStringAsync(url);
                _keys = new JsonWebKeySet(json).GetSigningKeys();
                _keysAt = Clock.RealUtcNow;
            }
            return _keys;
        }
        if (settings.JwtPublicKeyPem is { } pem)
        {
            var ec = ECDsa.Create();
            ec.ImportFromPem(pem);
            return [new ECDsaSecurityKey(ec)];
        }
        throw new UnauthorizedException("Token verification is not configured", "auth_not_configured");
    }
}

public static class AuthExtensions
{
    private const string Key = "metrorail.principal";

    private static string? Bearer(HttpContext ctx)
    {
        var h = ctx.Request.Headers.Authorization.ToString();
        return h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? h[7..] : null;
    }

    /// <summary>The logged-in user, or 401 "Log in to use this feature".</summary>
    public static async Task<Principal> RequireUserAsync(this HttpContext ctx)
    {
        return await ctx.OptionalUserAsync() ?? throw new UnauthorizedException("Log in to use this feature", "login_required");
    }

    public static async Task<Principal> RequireRoleAsync(this HttpContext ctx, string role)
    {
        var p = await ctx.RequireUserAsync();
        p.Require(role);
        return p;
    }

    public static async Task<Principal?> OptionalUserAsync(this HttpContext ctx)
    {
        if (ctx.Items.TryGetValue(Key, out var cached)) return (Principal?)cached;
        var token = Bearer(ctx);
        Principal? p = token is null ? null : await ctx.RequestServices.GetRequiredService<TokenVerifier>().VerifyAsync(token);
        ctx.Items[Key] = p;
        return p;
    }
}
