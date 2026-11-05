using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Common;
using DistrictSync.Contracts.Events;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace DistrictSync.Infrastructure.Messaging;

/// <summary>
/// Single confirm-mode channel shared by the process. Publisher confirms make <c>PublishAsync</c> complete
/// only once the broker has persisted the message, which is what lets the outbox dispatcher mark a row
/// processed with confidence.
/// </summary>
public sealed class RabbitMqPublisher(IConnection connection, ILogger<RabbitMqPublisher> logger) : IEphemeralEventPublisher, IAsyncDisposable
{
    public static readonly ActivitySource ActivitySource = new("DistrictSync.Messaging");
    private static readonly Meter Meter = new("DistrictSync.Messaging");
    private static readonly Counter<long> Published = Meter.CreateCounter<long>("districtsync.messages.published", description: "Messages confirmed by the broker");

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken ct)
    {
        var type = integrationEvent.GetType();
        await PublishAsync(
            EventCatalog.Exchange,
            EventCatalog.NameOf(type),
            integrationEvent.EventId,
            EventCatalog.NameOf(type),
            integrationEvent.TenantId,
            JsonSerializer.Serialize(integrationEvent, type, JsonDefaults.Options),
            Activity.Current?.Id,
            attempt: 0,
            extraHeaders: null,
            ct);
    }

    public async Task PublishAsync(string exchange, string routingKey, Guid messageId, string type, Guid tenantId, string payload, string? traceParent, int attempt, IDictionary<string, object?>? extraHeaders, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity($"publish {type}", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", exchange);
        activity?.SetTag("messaging.rabbitmq.destination.routing_key", routingKey);
        activity?.SetTag("messaging.message.id", messageId.ToString("D"));

        var headers = new Dictionary<string, object?>
        {
            [MessageHeaders.TenantId] = tenantId.ToString("D"),
            [MessageHeaders.Attempt] = attempt,
            [MessageHeaders.TraceParent] = activity?.Id ?? traceParent,
        };
        if (extraHeaders is not null)
        {
            foreach (var (k, v) in extraHeaders)
            {
                headers[k] = v;
            }
        }

        var properties = new BasicProperties
        {
            MessageId = messageId.ToString("D"),
            Type = type,
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            Persistent = true,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            Headers = headers,
        };

        var body = Encoding.UTF8.GetBytes(payload);
        await _gate.WaitAsync(ct);
        try
        {
            var channel = await GetChannelAsync(ct);
            await channel.BasicPublishAsync(exchange, routingKey, mandatory: false, basicProperties: properties, body: body, cancellationToken: ct);
            Published.Add(1, new KeyValuePair<string, object?>("type", type), new KeyValuePair<string, object?>("exchange", exchange));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Publish of {Type} {MessageId} failed; channel will be recreated", type, messageId);
            await ResetChannelAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        _channel = await connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        await RabbitMqTopology.EnsureCoreAsync(_channel, ct);
        return _channel;
    }

    private async Task ResetChannelAsync()
    {
        if (_channel is not null)
        {
            try
            {
                await _channel.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Ignoring error while disposing publisher channel");
            }

            _channel = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetChannelAsync();
        _gate.Dispose();
    }
}
