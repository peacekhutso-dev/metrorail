using System.Text;
using MetroRail.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MetroRail.Tests;

public class RealtimeTests
{
    private static Task<TestApp> Start(FakeCentrifugo fake) =>
        TestApp.StartAsync(RealtimeService.CreateBuilder, RealtimeService.Build, "realtime", b => b.Services.AddSingleton<ICentrifugo>(fake));

    [Fact]
    public async Task Token_channels_for_guest_and_user()
    {
        await using var t = await Start(new FakeCentrifugo());
        var (_, guest) = await t.Client.Get("/token?trains=3412");
        Assert.Equal(["trains:3412"], guest.GetProperty("channels").EnumerateArray().Select(c => c.GetString()));
        var (_, user) = await t.Client.Get("/token?trains=3412,3420", "u1");
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(user.Str("token"), new TokenValidationParameters
        {
            ValidateIssuer = false, ValidateAudience = false,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("dev-token-secret-change-me-32-bytes!!")),
        });
        Assert.True(result.IsValid);
        Assert.Equal(["trains:3412", "trains:3420", "alerts:#u1"], user.GetProperty("channels").EnumerateArray().Select(c => c.GetString()));
    }

    [Fact]
    public async Task Events_are_forwarded_to_channels()
    {
        var fake = new FakeCentrifugo();
        await using var t = await Start(fake);
        await t.Bus.PublishAsync("tracking.train.state_changed", new { train_no = "3412", state = "stopped", stationary_s = 310 });
        await t.Bus.PublishAsync("notifications.alert.created", new { user_id = "u1", id = "ALR-1", kind = "stopped", title = "t", body = "b", train_no = "3412" });
        Assert.Equal("trains:3412", fake.Sent[0].Channel);
        Assert.Equal("stopped", fake.Sent[0].Data.Str("state"));
        Assert.Equal("alerts:#u1", fake.Sent[1].Channel);
    }
}
