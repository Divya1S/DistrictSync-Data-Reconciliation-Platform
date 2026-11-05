using System.Diagnostics;
using System.Diagnostics.Metrics;
using DistrictSync.Application.Messaging;
using DistrictSync.Application.Tenancy;
using DistrictSync.Contracts.Events;
using DistrictSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistrictSync.Infrastructure.Messaging;

/// <summary>
/// Relays committed outbox rows to RabbitMQ. Rows are claimed with a lease so several dispatcher
/// instances can run; a confirmed publish marks the row processed. Delivery is therefore at-least-once:
/// a crash between publish and mark re-publishes the message, and the consumer inbox absorbs the duplicate.
/// </summary>
public sealed class OutboxDispatcher(IServiceProvider services, RabbitMqPublisher publisher, IOptions<MessagingOptions> options, TimeProvider clock, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly Meter Meter = new("DistrictSync.Messaging");
    private static readonly Counter<long> Dispatched = Meter.CreateCounter<long>("districtsync.outbox.dispatched");
    private static readonly Histogram<double> Lag = Meter.CreateHistogram<double>("districtsync.outbox.lag", unit: "ms", description: "Time from OccurredAt to confirmed publish");

    private readonly string _owner = Truncate($"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}", 100);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox dispatcher {Owner} started (poll {Interval}, batch {Batch})", _owner, options.Value.OutboxPollInterval, options.Value.OutboxBatchSize);
        using var timer = new PeriodicTimer(options.Value.OutboxPollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await DispatchBatchAsync(stoppingToken);
                if (dispatched > 0)
                {
                    continue; // drain quickly while there is work
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch cycle failed; will retry");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetSystem("outbox-dispatcher");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow();
        var until = now + options.Value.OutboxLease;

        var sql = $"""
            UPDATE TOP (@p0) o
               SET LockOwner = @p1, LockExpiresAt = @p2
            OUTPUT inserted.Id
              FROM [{AppDbContext.Schema}].[OutboxMessages] o WITH (READPAST, UPDLOCK, ROWLOCK)
             WHERE o.ProcessedAt IS NULL
               AND (o.NextAttemptAt IS NULL OR o.NextAttemptAt <= @p3)
               AND (o.LockExpiresAt IS NULL OR o.LockExpiresAt <= @p3)
            """;

        var ids = await db.Database.SqlQueryRaw<Guid>(sql, options.Value.OutboxBatchSize, _owner, until, now).ToListAsync(ct);
        if (ids.Count == 0)
        {
            return 0;
        }

        var messages = await db.OutboxMessages.Where(m => ids.Contains(m.Id)).OrderBy(m => m.OccurredAt).ToListAsync(ct);
        var published = 0;
        foreach (var message in messages)
        {
            try
            {
                await publisher.PublishAsync(EventCatalog.Exchange, message.Type, message.Id, message.Type, message.TenantId.Value, message.Payload, message.TraceParent, attempt: 0, extraHeaders: null, ct);
                var processedAt = clock.GetUtcNow();
                message.MarkProcessed(processedAt);
                Dispatched.Add(1, new KeyValuePair<string, object?>("type", message.Type));
                Lag.Record((processedAt - message.OccurredAt).TotalMilliseconds);
                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.MarkFailed(ex.Message, clock.GetUtcNow());
                logger.LogWarning(ex, "Outbox message {MessageId} ({Type}) failed to publish (attempt {Attempts})", message.Id, message.Type, message.Attempts);
            }
        }

        await db.SaveChangesAsync(ct);
        return published;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
