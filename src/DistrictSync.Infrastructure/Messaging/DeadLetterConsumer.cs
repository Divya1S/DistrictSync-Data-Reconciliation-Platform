using System.Text;
using DistrictSync.Application.Messaging;
using DistrictSync.Application.Operations;
using DistrictSync.Application.Tenancy;
using DistrictSync.Contracts.Events;
using DistrictSync.Domain.Common;
using DistrictSync.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DistrictSync.Infrastructure.Messaging;

/// <summary>Drains the dead-letter queue into the database so operators can inspect and replay from the dashboard.</summary>
public sealed class DeadLetterConsumer(IConnection connection, IServiceProvider services, RabbitMqPublisher publisher, TimeProvider clock, ILogger<DeadLetterConsumer> logger) : BackgroundService
{
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await RabbitMqTopology.EnsureCoreAsync(_channel, stoppingToken);
        await _channel.BasicQosAsync(0, 8, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                await ParkAsync(ea, stoppingToken);
                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist dead letter {MessageId}; leaving it on the queue", ea.BasicProperties.MessageId);
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
            }
        };

        await _channel.BasicConsumeAsync(EventCatalog.DeadLetterQueue, autoAck: false, consumer, stoppingToken);
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task ParkAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var headers = ea.BasicProperties.Headers;
        var tenantId = TenantId.TryParse(MessageHeaders.GetString(headers, MessageHeaders.TenantId), out var t) ? t : (TenantId?)null;
        var messageId = Guid.TryParse(ea.BasicProperties.MessageId, out var id) ? id : (Guid?)null;
        var queue = MessageHeaders.GetString(headers, MessageHeaders.OriginQueue) ?? "(broker)";
        var reason = MessageHeaders.GetString(headers, MessageHeaders.DeadReason) ?? "Rejected by consumer.";
        var routingKey = MessageHeaders.GetString(headers, MessageHeaders.OriginalRoutingKey) ?? ea.RoutingKey;
        var type = ea.BasicProperties.Type ?? routingKey;
        var attempts = MessageHeaders.GetInt(headers, MessageHeaders.Attempt);
        var payload = Encoding.UTF8.GetString(ea.Body.Span);
        var now = clock.GetUtcNow();

        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetSystem("dead-letter-consumer");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var parked = DeadLetterMessage.Park(messageId, tenantId, queue, routingKey, type, payload, reason, attempts, now);
        db.DeadLetters.Add(parked);
        await db.SaveChangesAsync(ct);

        logger.LogError("Dead letter parked: {Type} {MessageId} from {Queue} after {Attempts} attempt(s): {Reason}", type, messageId, queue, attempts, reason);

        if (tenantId is not null)
        {
            await publisher.PublishAsync(new MessageDeadLetteredEvent(Guid.CreateVersion7(), tenantId.Value.Value, now, parked.Id, queue, type, reason), ct);
        }
    }
}

public sealed class DeadLetterReplayer(RabbitMqPublisher publisher) : IDeadLetterReplayer
{
    public Task ReplayAsync(DeadLetterMessage message, CancellationToken ct) =>
        publisher.PublishAsync(EventCatalog.Exchange, message.RoutingKey, message.MessageId ?? Guid.CreateVersion7(), message.MessageType, message.TenantId?.Value ?? Guid.Empty, message.Payload, null, attempt: 0, extraHeaders: null, ct);
}
