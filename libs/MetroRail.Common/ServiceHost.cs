using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scalar.AspNetCore;

namespace MetroRail.Common;

/// <summary>
/// App factory: every service gets the same logging, JSON, errors, auth, health checks, OpenAPI,
/// database and event bus.
///
///   var builder = ServiceHost.CreateBuilder(args, settings);
///   builder.Services.AddServiceDb&lt;NetworkDb&gt;(settings);
///   var app = ServiceHost.Build&lt;NetworkDb&gt;(builder);
///   app.MapNetworkEndpoints();
/// </summary>
public static class ServiceHost
{
    public static WebApplicationBuilder CreateBuilder(string[] args, ServiceSettings settings)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = args,
            EnvironmentName = settings.IsProduction ? "Production" : "Development",
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(o =>
        {
            o.IncludeScopes = true;
            o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
            o.UseUtcTimestamp = true;
        });
        builder.Logging.SetMinimumLevel(settings.Environment == "test" ? LogLevel.Warning : LogLevel.Information);

        builder.Services.ConfigureHttpJsonOptions(o => Json.Configure(o.SerializerOptions));
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<IClock>(new Clock());
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<TokenVerifier>();
        builder.Services.AddOpenApi();
        builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
            p.WithOrigins(settings.CorsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

        if (settings.EventBus == "nats")
        {
            builder.Services.AddSingleton<NatsEventBus>();
            builder.Services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<NatsEventBus>());
        }
        else
        {
            builder.Services.AddSingleton<InMemoryEventBus>();
            builder.Services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<InMemoryEventBus>());
        }
        return builder;
    }

    /// <summary>Builds the app. Pass the service's DbContext type, or <see cref="NoDb"/>.</summary>
    public static WebApplication Build<TDb>(WebApplicationBuilder builder) where TDb : class
    {
        var app = builder.Build();
        var settings = app.Services.GetRequiredService<ServiceSettings>();

        app.Use(async (ctx, next) =>
        {
            var rid = ctx.Request.Headers["x-request-id"].ToString();
            ctx.TraceIdentifier = string.IsNullOrEmpty(rid) ? Guid.NewGuid().ToString("N") : rid;
            ctx.Response.Headers["x-request-id"] = ctx.TraceIdentifier;
            using (app.Logger.BeginScope(new Dictionary<string, object> { ["request_id"] = ctx.TraceIdentifier, ["service"] = settings.ServiceName }))
                await next(ctx);
        });
        app.Use((ctx, next) => ErrorMiddleware.Handle(ctx, () => next(ctx)));
        app.UseCors();

        app.MapGet("/health", () => new { status = "ok", service = settings.ServiceName, version = "0.2.0" })
            .WithTags("ops").ExcludeFromDescription();
        app.MapGet("/ready", async (HttpContext ctx) =>
        {
            if (typeof(TDb) != typeof(NoDb))
            {
                var db = (DbContext)ctx.RequestServices.GetRequiredService(typeof(TDb));
                await db.Database.ExecuteSqlRawAsync("SELECT 1");
            }
            return new { status = "ready" };
        }).WithTags("ops").ExcludeFromDescription();
        app.MapOpenApi("/openapi.json");
        app.MapScalarApiReference("/docs", o => o.WithOpenApiRoutePattern("/openapi.json")
            .WithTitle($"MetroRail {settings.ServiceName} service"));

        // Create tables outside production (production: EF Core migrations), then start the event bus.
        if (typeof(TDb) != typeof(NoDb) && !settings.IsProduction)
        {
            using var scope = app.Services.CreateScope();
            var db = (DbContext)scope.ServiceProvider.GetRequiredService(typeof(TDb));
            db.Database.EnsureCreated();
        }
        return app;
    }

    /// <summary>Starts NATS subscriptions (after handlers are registered) and runs the app.</summary>
    public static async Task StartBusAsync(WebApplication app)
    {
        if (app.Services.GetService<NatsEventBus>() is { } nats) await nats.StartAsync(CancellationToken.None);
    }
}

/// <summary>Marker for services without a database (eta, realtime).</summary>
public sealed class NoDb;
