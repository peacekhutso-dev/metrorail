using MetroRail.Common;

namespace MetroRail.Eta;

public static class EtaService
{
    public static WebApplicationBuilder CreateBuilder(string[] args, ServiceSettings? settings = null)
    {
        settings ??= new ServiceSettings("eta");
        var b = ServiceHost.CreateBuilder(args, settings);
        b.Services.AddKeyedSingleton<IServiceClient>("network", (_, _) => new ServiceClient("network"));
        b.Services.AddKeyedSingleton<IServiceClient>("tracking", (_, _) => new ServiceClient("tracking"));
        // ETA_MODEL_PATH=/models/eta.onnx enables the trained model; otherwise the baseline.
        b.Services.AddSingleton<IPredictor>(sp => Predictors.Load(settings.Get("ETA_MODEL_PATH"), sp.GetRequiredService<ILogger<OnnxPredictor>>()));
        return b;
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        var app = ServiceHost.Build<NoDb>(builder);
        app.MapEta();
        return app;
    }
}

public static class Program
{
    public static async Task Main(string[] args)
    {
        var app = EtaService.Build(EtaService.CreateBuilder(args));
        await ServiceHost.StartBusAsync(app);
        await app.RunAsync();
    }
}
