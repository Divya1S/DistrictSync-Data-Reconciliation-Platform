using DistrictSync.Infrastructure.Persistence;

namespace DistrictSync.Worker;

/// <summary>Applies migrations / seed data before the message consumers start (hosted services start in registration order).</summary>
public sealed class StartupInitializer(DatabaseInitializer initializer, ILogger<StartupInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Worker starting: checking database schema");
        await initializer.InitializeAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
