using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MetroRail.Common;

/// <summary>
/// Each service owns its own database (no shared tables). DATABASE_URL may be:
///   postgresql://user:pass@host:5432/db   (docker-compose / production)
///   sqlite:path/to/file.db                (no-Docker dev)
///   sqlite::memory:                       (tests; one shared in-memory DB per app)
/// Local/test/staging create tables on start; production uses EF Core migrations (see docs).
/// </summary>
public static class Db
{
    public static IServiceCollection AddServiceDb<TContext>(this IServiceCollection services, ServiceSettings settings)
        where TContext : DbContext
    {
        var url = settings.DatabaseUrl;
        if (url.StartsWith("sqlite:"))
        {
            var path = url["sqlite:".Length..];
            string cs;
            if (path == ":memory:")
            {
                cs = $"Data Source=file:mem-{Guid.NewGuid():N}?mode=memory&cache=shared";
                var keepAlive = new SqliteConnection(cs); // keeps the in-memory DB alive for the app's lifetime
                keepAlive.Open();
                services.AddSingleton(new SqliteKeepAlive(keepAlive));
            }
            else
            {
                cs = $"Data Source={path}";
            }
            services.AddDbContext<TContext>(o => o.UseSqlite(cs));
        }
        else
        {
            services.AddDbContext<TContext>(o => o.UseNpgsql(ToNpgsql(url)));
        }
        return services;
    }

    public static string ToNpgsql(string url)
    {
        if (!url.Contains("://")) return url; // already a connection string
        var u = new Uri(url.Replace("postgresql+asyncpg://", "postgresql://"));
        var userInfo = u.UserInfo.Split(':', 2);
        return $"Host={u.Host};Port={(u.Port > 0 ? u.Port : 5432)};Database={u.AbsolutePath.Trim('/')};" +
               $"Username={Uri.UnescapeDataString(userInfo[0])};Password={(userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "")}";
    }

    public sealed class SqliteKeepAlive(SqliteConnection connection) : IDisposable
    {
        public void Dispose() => connection.Dispose();
    }
}
