using MetroRail.Common;
using MetroRail.Identity;

namespace MetroRail.Tests;

public class IdentityTests
{
    private static Task<TestApp> Start() => TestApp.StartAsync(IdentityService.CreateBuilder, IdentityService.Build, "identity");

    private static async Task<(System.Text.Json.JsonElement Start, int Status, System.Text.Json.JsonElement Tokens)> Login(TestApp t, string phone = "082 456 4567")
    {
        var (_, start) = await t.Client.Post("/auth/start", new { phone });
        var (status, tok) = await t.Client.Post("/auth/verify", new { challenge_id = start.Str("challenge_id"), code = start.Str("dev_code") });
        return (start, status, tok);
    }

    [Fact]
    public async Task Phone_login_issues_token_any_service_can_verify()
    {
        await using var t = await Start();
        var (start, status, tok) = await Login(t);
        Assert.Equal("+27 82 *** 4567", start.Str("sent_to"));
        Assert.Equal(200, status);
        Assert.True(tok.GetProperty("is_new_user").GetBoolean());

        // Another service only needs the public key (no dev tokens allowed).
        var other = new ServiceSettings("trips", new Dictionary<string, string?> { ["ALLOW_DEV_TOKENS"] = "false" })
            { JwtPublicKeyPem = t.Get<Signer>().PublicPem };
        var verifier = new TokenVerifier(other, null!);
        Assert.Equal(tok.Str("user_id"), (await verifier.VerifyAsync(tok.Str("access_token"))).UserId);

        var (_, _, again) = await Login(t);
        Assert.Equal(tok.Str("user_id"), again.Str("user_id"));
        Assert.False(again.GetProperty("is_new_user").GetBoolean());
    }

    [Fact]
    public async Task Wrong_code_counts_down_then_locks()
    {
        await using var t = await Start();
        var (_, start) = await t.Client.Post("/auth/start", new { phone = "0824564567" });
        var wrong = start.Str("dev_code") == "000000" ? "111111" : "000000";
        var codes = new List<string>();
        for (var i = 0; i < 4; i++)
            codes.Add((await t.Client.Post("/auth/verify", new { challenge_id = start.Str("challenge_id"), code = wrong })).Body.GetProperty("error").Str("code"));
        Assert.Equal(["code_mismatch", "code_mismatch", "code_mismatch", "too_many_attempts"], codes);
    }

    [Fact]
    public async Task Real_token_works_on_me_consents_refresh_and_delete()
    {
        await using var t = await Start();
        var (_, _, tok) = await Login(t);
        var user = tok.Str("user_id");
        var real = new HttpRequestMessage(HttpMethod.Get, "/me");
        real.Headers.Add("Authorization", "Bearer " + tok.Str("access_token"));
        Assert.Equal(System.Net.HttpStatusCode.OK, (await t.Client.SendAsync(real)).StatusCode);

        var (_, me) = await t.Client.Get("/me", user);
        Assert.False(me.GetProperty("consents").GetProperty("nearest_station").GetBoolean());
        var (_, updated) = await t.Client.Send(HttpMethod.Put, "/me/consents", new { trip_alerts = true, nearest_station = true, usage_counts = false }, user);
        Assert.True(updated.GetProperty("consents").GetProperty("nearest_station").GetBoolean());

        Assert.Equal(200, (await t.Client.Post("/auth/refresh", new { refresh_token = tok.Str("refresh_token") })).Status);
        Assert.Equal(401, (await t.Client.Post("/auth/refresh", new { refresh_token = tok.Str("refresh_token") })).Status); // rotated

        Assert.Equal(204, (await t.Client.Send(HttpMethod.Delete, "/me", null, user)).Status);
        Assert.Contains("identity.user.deleted", t.Bus.PublishedSubjects);
        Assert.Equal(404, (await t.Client.Get("/me", user)).Status);
    }

    [Fact]
    public async Task Jwks_published()
    {
        await using var t = await Start();
        var (_, jwks) = await t.Client.Get("/.well-known/jwks.json");
        var k = jwks.GetProperty("keys")[0];
        Assert.Equal("EC", k.Str("kty"));
        Assert.Equal("ES256", k.Str("alg"));
    }
}
