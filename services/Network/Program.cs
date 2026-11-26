
using MetroRail.Common;

namespace MetroRail.Network;

public static class NetworkService
{
    /// <summary>
    /// Configures the Network microservice.
    /// </summary>
    public static WebApplicationBuilder CreateBuilder(
        string[] args,
        ServiceSettings? settings = null)
    {
        settings ??= new ServiceSettings("network");

        var builder = ServiceHost.CreateBuilder(
            args,
            settings);

        builder.Services.AddServiceDb<NetworkDb>(settings);

        return builder;
    }

    /// <summary>
    /// Builds the service and initialises the railway network.
    /// </summary>
    public static WebApplication Build(
        WebApplicationBuilder builder)
    {
        var app = ServiceHost.Build<NetworkDb>(builder);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<NetworkDb>();

            Seed.SeedIfEmptyAsync(db)
                .GetAwaiter()
                .GetResult();
        }

        app.MapNetwork();

        return app;
    }
}

public static class Program
{
    public static async Task Main(string[] args)
    {
        // Timetable import is an explicit administrative operation.
        var importTimetable = args.Contains(
            "--import-timetable",
            StringComparer.OrdinalIgnoreCase);

        // Remove our custom argument before configuring ASP.NET.
        var hostArgs = args
            .Where(arg => !arg.Equals(
                "--import-timetable",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var app = NetworkService.Build(
            NetworkService.CreateBuilder(hostArgs));

        if (importTimetable)
        {
            await using (app)
            {
                // Import the timetable without starting the web server.
                using var scope = app.Services.CreateScope();

                var db = scope.ServiceProvider
                    .GetRequiredService<NetworkDb>();

                var count =
                    await TimetableImporter.ImportAsync(db);

                app.Logger.LogInformation(
                    "Successfully imported {Count} weekday train services.",
                    count);
            }

            return;
        }

        // Normal microservice startup.
        await ServiceHost.StartBusAsync(app);

        await app.RunAsync();
    }
}
