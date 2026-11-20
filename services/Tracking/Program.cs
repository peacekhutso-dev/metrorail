using MetroRail.Common;

namespace MetroRail.Tracking;

public static class TrackingService
{
    public static WebApplicationBuilder CreateBuilder(string[] args, ServiceSettings? settings = null)
    {
        settings ??= new ServiceSettings("tracking");
        var b = ServiceHost.CreateBuilder(args, settings);
        b.Services.AddServiceDb<TrackingDb>(settings);
        b.Services.AddKeyedSingleton<IServiceClient>("network", (_, _) => new ServiceClient("network"));
        b.Services.AddSingleton<TripCache>();
        return b;
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        var app = ServiceHost.Build<TrackingDb>(builder);
        app.MapTracking();
        return app;
    }
}

public static class Program
{
    public static async Task Main(string[] args)
    {
        var app = TrackingService.Build(TrackingService.CreateBuilder(args));
        await ServiceHost.StartBusAsync(app);
        await app.RunAsync();
    }
}
