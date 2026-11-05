using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Common;
using DistrictSync.Application.Messaging;
using DistrictSync.Application.Tenancy;
using DistrictSync.Contracts.Events;
using DistrictSync.Domain.Common;
using DistrictSync.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client.Events;

namespace DistrictSync.Infrastructure.Messaging;

public enum DeliveryVerdict
{
    Ack,
    Retry,
    DeadLetter,
    Requeue,
}

/// <summary>
/// The per-message consumer pipeline: decode → trace → tenant scope → inbox de-duplication → handler →
/// ack, with retry tiers and dead-lettering on failure. This is where "process the same event many times,
/// cause the business effect once" is enforced.
/// </summary>
public sealed class MessagePipeline(IServiceProvider services, RabbitMqPublisher publisher, IOptions<MessagingOptions> options, TimeProvider clock, ILogger<MessagePipeline> logger)
{
    private static readonly Meter Meter = new("DistrictSync.Messaging");
    private static readonly Counter<long> Consumed = Meter.CreateCounter<long>("districtsync.messages.consumed");
    private static readonly Counter<long> Duplicates = Meter.CreateCounter<long>("districtsync.messages.duplicates", description: "Deliveries skipped because the inbox already had them");
    private static readonly Counter<long> Retried = Meter.CreateCounter<long>("districtsync.messages.retried");
    private static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>("districtsync.messages.dead_lettered");
    private static readonly Histogram<double> HandleDuration = Meter.CreateHistogram<double>("districtsync.messages.handle_duration", unit: "ms");

    public async Task<DeliveryVerdict> HandleAsync(string queueName, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        var headers = delivery.BasicProperties.Headers;
        var typeName = delivery.BasicProperties.Type ?? delivery.RoutingKey;
        var attempt = MessageHeaders.GetInt(headers, MessageHeaders.Attempt);
        var traceParent = MessageHeaders.GetString(headers, MessageHeaders.TraceParent);
        var tenantRaw = MessageHeaders.GetString(headers, MessageHeaders.TenantId);
        var messageIdOk = Guid.TryParse(delivery.BasicProperties.MessageId, out var messageId);
        var payload = Encoding.UTF8.GetString(delivery.Body.Span);

        ActivityContext.TryParse(traceParent, null, out var parentContext);
        using var activity = RabbitMqPublisher.ActivitySource.StartActivity($"consume {typeName}", ActivityKind.Consumer, parentContext);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", queueName);
        activity?.SetTag("messaging.message.id", delivery.BasicProperties.MessageId);
        activity?.SetTag("districtsync.attempt", attempt);

        var tags = new TagList { { "queue", queueName }, { "type", typeName } };
        Consumed.Add(1, tags);

        if (!messageIdOk || !TenantId.TryParse(tenantRaw, out var tenantId))
        {
            return await DeadLetterAsync(queueName, delivery, typeName, payload, "Message lacks a valid message-id or x-tenant-id header.", attempt, ct);
        }

        var clrType = EventCatalog.Resolve(typeName);
        if (clrType is null)
        {
            return await DeadLetterAsync(queueName, delivery, typeName, payload, $"Unknown event type '{typeName}'.", attempt, ct);
        }

        IIntegrationEvent integrationEvent;
        try
        {
            integrationEvent = (IIntegrationEvent?)JsonSerializer.Deserialize(payload, clrType, JsonDefaults.Options) ?? throw new JsonException("Payload deserialized to null.");
        }
        catch (JsonException ex)
        {
            return await DeadLetterAsync(queueName, delivery, typeName, payload, $"Malformed payload: {ex.Message}", attempt, ct);
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var scope = services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetSystemForTenant(tenantId, $"consumer:{queueName}");

            var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(clrType);
            var handler = scope.ServiceProvider.GetService(handlerType);
            if (handler is null)
            {
                logger.LogDebug("No handler for {Type} in queue {Queue}; acknowledging", typeName, queueName);
                return DeliveryVerdict.Ack;
            }

            var mode = (HandlerMode)handlerType.GetProperty(nameof(IIntegrationEventHandler<IIntegrationEvent>.Mode))!.GetValue(handler)!;
            var invoke = handlerType.GetMethod(nameof(IIntegrationEventHandler<IIntegrationEvent>.HandleAsync))!;
            Func<CancellationToken, Task> handle = token => (Task)invoke.Invoke(handler, [integrationEvent, token])!;

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var consumer = queueName;
            var duplicate = mode switch
            {
                HandlerMode.Transactional => await RunTransactionalAsync(db, messageId, consumer, typeName, handle, ct),
                HandlerMode.LongRunning => await RunLongRunningAsync(db, messageId, consumer, typeName, handle, ct),
                _ => await RunWithoutInboxAsync(handle, ct),
            };

            if (duplicate)
            {
                Duplicates.Add(1, tags);
                logger.LogInformation("Duplicate delivery of {Type} {MessageId} to {Queue} skipped", typeName, messageId, queueName);
            }

            HandleDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tags);
            return DeliveryVerdict.Ack;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return DeliveryVerdict.Requeue;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            HandleDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tags);
            var nextAttempt = attempt + 1;
            if (nextAttempt >= options.Value.MaxAttempts)
            {
                logger.LogError(ex, "{Type} {MessageId} failed on attempt {Attempt}; dead-lettering", typeName, messageId, nextAttempt);
                return await DeadLetterAsync(queueName, delivery, typeName, payload, $"{ex.GetType().Name}: {ex.Message}", nextAttempt, ct);
            }

            var tier = RabbitMqTopology.TierForAttempt(nextAttempt);
            logger.LogWarning(ex, "{Type} {MessageId} failed on attempt {Attempt}; retrying via {Tier} tier", typeName, messageId, nextAttempt, tier);
            try
            {
                await publisher.PublishAsync(RabbitMqTopology.RetryExchange(tier), queueName, messageId, typeName, tenantId.Value, payload, traceParent, nextAttempt, extraHeaders: null, CancellationToken.None);
                Retried.Add(1, tags);
                return DeliveryVerdict.Ack;
            }
            catch (Exception publishEx)
            {
                logger.LogError(publishEx, "Could not schedule retry for {MessageId}; requeueing", messageId);
                return DeliveryVerdict.Requeue;
            }
        }
    }

    /// <summary>Inbox claim + handler + completion in one database transaction: exactly-once business effect.</summary>
    private async Task<bool> RunTransactionalAsync(AppDbContext db, Guid messageId, string consumer, string type, Func<CancellationToken, Task> handle, CancellationToken ct)
    {
        var duplicate = false;
        try
        {
            await db.ExecuteInTransactionAsync(async token =>
            {
                var existing = await db.InboxMessages.FirstOrDefaultAsync(m => m.MessageId == messageId && m.Consumer == consumer, token);
                if (existing is not null)
                {
                    duplicate = true;
                    return;
                }

                var inbox = InboxMessage.Claim(messageId, consumer, type, clock.GetUtcNow());
                db.InboxMessages.Add(inbox);
                await handle(token);
                inbox.MarkProcessed(clock.GetUtcNow());
                await db.SaveChangesAsync(token);
            }, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Two consumers raced on the same message; the other one won. Nothing to do.
            return true;
        }

        return duplicate;
    }

    /// <summary>
    /// Claim (with lease) first, then run with the handler's own transactions, then complete. A concurrent
    /// duplicate sees the live lease and is dropped; an expired lease means a crashed attempt and the
    /// handler (which must be resumable) runs again.
    /// </summary>
    private async Task<bool> RunLongRunningAsync(AppDbContext db, Guid messageId, string consumer, string type, Func<CancellationToken, Task> handle, CancellationToken ct)
    {
        var lease = options.Value.LongRunningLease;
        var now = clock.GetUtcNow();
        var existing = await db.InboxMessages.FirstOrDefaultAsync(m => m.MessageId == messageId && m.Consumer == consumer, ct);
        if (existing is { IsProcessed: true })
        {
            return true;
        }

        if (existing is not null && existing.IsLeased(now))
        {
            logger.LogInformation("{Type} {MessageId} is being processed by another consumer (lease until {Lease}); dropping duplicate delivery", type, messageId, existing.LeaseExpiresAt);
            return true;
        }

        try
        {
            if (existing is null)
            {
                db.InboxMessages.Add(InboxMessage.Claim(messageId, consumer, type, now, lease));
            }
            else
            {
                existing.Reclaim(now, lease);
                logger.LogWarning("Resuming {Type} {MessageId}: previous attempt did not complete (attempt {Attempt})", type, messageId, existing.Attempts);
            }

            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return true;
        }

        db.ClearTracking();
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = HeartbeatAsync(messageId, consumer, lease, heartbeatCts.Token);
        var completed = false;
        try
        {
            await handle(ct);
            db.ClearTracking();
            var row = await db.InboxMessages.FirstAsync(m => m.MessageId == messageId && m.Consumer == consumer, ct);
            row.MarkProcessed(clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            completed = true;
            return false;
        }
        finally
        {
            await heartbeatCts.CancelAsync();
            try
            {
                await heartbeat;
            }
            catch (OperationCanceledException)
            {
                // expected
            }

            if (!completed)
            {
                await ReleaseLeaseAsync(messageId, consumer);
            }
        }
    }

    private async Task HeartbeatAsync(Guid messageId, string consumer, TimeSpan lease, CancellationToken ct)
    {
        var interval = lease / 3;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(interval, ct);
            await using var scope = services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetSystem("inbox-heartbeat");
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var until = clock.GetUtcNow() + lease;
            await db.InboxMessages.Where(m => m.MessageId == messageId && m.Consumer == consumer && m.ProcessedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.LeaseExpiresAt, until), ct);
        }
    }

    /// <summary>Best effort: let a redelivery resume immediately instead of waiting for the lease to lapse.</summary>
    private async Task ReleaseLeaseAsync(Guid messageId, string consumer)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetSystem("inbox-release");
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.InboxMessages.Where(m => m.MessageId == messageId && m.Consumer == consumer && m.ProcessedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.LeaseExpiresAt, (DateTimeOffset?)null), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not release inbox lease for {MessageId}; it will expire on its own", messageId);
        }
    }

    private static async Task<bool> RunWithoutInboxAsync(Func<CancellationToken, Task> handle, CancellationToken ct)
    {
        await handle(ct);
        return false;
    }

    private async Task<DeliveryVerdict> DeadLetterAsync(string queueName, BasicDeliverEventArgs delivery, string type, string payload, string reason, int attempts, CancellationToken ct)
    {
        DeadLettered.Add(1, new TagList { { "queue", queueName }, { "type", type } });
        var tenant = MessageHeaders.GetString(delivery.BasicProperties.Headers, MessageHeaders.TenantId);
        var messageId = Guid.TryParse(delivery.BasicProperties.MessageId, out var parsedId) ? parsedId : Guid.Empty;
        try
        {
            await publisher.PublishAsync(
                EventCatalog.DeadLetterExchange,
                delivery.RoutingKey,
                messageId == Guid.Empty ? Guid.CreateVersion7() : messageId,
                type,
                Guid.TryParse(tenant, out var t) ? t : Guid.Empty,
                payload,
                MessageHeaders.GetString(delivery.BasicProperties.Headers, MessageHeaders.TraceParent),
                attempts,
                new Dictionary<string, object?>
                {
                    [MessageHeaders.OriginQueue] = queueName,
                    [MessageHeaders.DeadReason] = reason,
                    [MessageHeaders.OriginalRoutingKey] = delivery.RoutingKey,
                },
                CancellationToken.None);
            return DeliveryVerdict.Ack;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish {MessageId} to the dead-letter exchange; using broker-side dead-lettering", messageId);
            return DeliveryVerdict.DeadLetter;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is SqlException { Number: 2627 or 2601 };
}
