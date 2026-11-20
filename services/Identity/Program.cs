using MetroRail.Common;

namespace MetroRail.Identity;

public static class IdentityService
{
    public static WebApplicationBuilder CreateBuilder(string[] args, ServiceSettings? settings = null)
    {
        settings ??= new ServiceSettings("identity");
        var s = settings;
        if (s.IsProduction && string.IsNullOrWhiteSpace(s.Get("SIGNING_KEY")))
            throw new InvalidOperationException("SIGNING_KEY (IDENTITY_SIGNING_KEY) is required in production");
        var b = ServiceHost.CreateBuilder(args, s);
        b.Services.AddServiceDb<IdentityDb>(s);
        b.Services.AddSingleton(new Signer(s.Get("SIGNING_KEY"), s.JwtIssuer, s.JwtAudience));
        b.Services.AddSingleton<ICodeSender, LogCodeSender>();
        return b;
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        var app = ServiceHost.Build<IdentityDb>(builder);
        // Identity verifies its own tokens without calling itself over HTTP.
        var settings = app.Services.GetRequiredService<ServiceSettings>();
        settings.JwtPublicKeyPem ??= app.Services.GetRequiredService<Signer>().PublicPem;
        app.MapIdentity();
        return app;
    }
}

public static class Program
{
    public static async Task Main(string[] args)
    {
        var app = IdentityService.Build(IdentityService.CreateBuilder(args));
        await ServiceHost.StartBusAsync(app);
        await app.RunAsync();
    }
}
