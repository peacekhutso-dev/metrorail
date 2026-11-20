using MetroRail.Common;

namespace MetroRail.Network;

public static class NetworkService
{
    public static WebApplicationBuilder CreateBuilder(string[] args, ServiceSettings? settings = null)
    {
        settings ??= new ServiceSettings("network");
        var b = ServiceHost.CreateBuilder(args, settings);
        b.Services.AddServiceDb<NetworkDb>(settings);
        return b;
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        var app = ServiceHost.Build<NetworkDb>(builder);
        using (var scope = app.Services.CreateScope())
            Seed.SeedIfEmptyAsync(scope.ServiceProvider.GetRequiredService<NetworkDb>()).GetAwaiter().GetResult();
        app.MapNetwork();
        return app;
    }
}

public static class Program
{
    public static async Task Main(string[] args)
    {
        var app = NetworkService.Build(NetworkService.CreateBuilder(args));
        await ServiceHost.StartBusAsync(app);
        await app.RunAsync();
    }
}
