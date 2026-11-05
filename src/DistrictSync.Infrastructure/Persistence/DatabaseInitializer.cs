using DistrictSync.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistrictSync.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    public bool MigrateOnStartup { get; set; }

    public bool SeedDemoData { get; set; }
}

/// <summary>
/// Applies migrations (and optionally demo data) at startup. Several processes (API, worker replicas) may
/// start simultaneously, so the work is serialised with a session-scoped SQL Server application lock held
/// on the context's open connection. EF Core manages the migration transactions itself.
/// </summary>
public sealed class DatabaseInitializer(IServiceProvider services, IOptions<DatabaseOptions> options, ILogger<DatabaseInitializer> logger)
{
    private const string LockResource = "districtsync-migrations";

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (!options.Value.MigrateOnStartup)
        {
            return;
        }

        using var scope = services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<Application.Tenancy.TenantContext>();
        tenant.SetSystem("db-initializer");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync($"EXEC sp_getapplock @Resource = '{LockResource}', @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 120000;", ct);
            try
            {
                var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
                if (pending.Count > 0)
                {
                    logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                    await db.Database.MigrateAsync(ct);
                }
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync($"EXEC sp_releaseapplock @Resource = '{LockResource}', @LockOwner = 'Session';", CancellationToken.None);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        if (options.Value.SeedDemoData)
        {
            await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(ct);
        }
    }
}
