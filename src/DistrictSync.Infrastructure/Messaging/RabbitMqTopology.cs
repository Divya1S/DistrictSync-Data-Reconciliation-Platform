using DistrictSync.Contracts.Events;
using RabbitMQ.Client;

namespace DistrictSync.Infrastructure.Messaging;

/// <summary>A queue this process consumes and the routing keys bound to it.</summary>
public sealed record QueueSubscription(string QueueName, IReadOnlyList<string> RoutingKeys, bool Exclusive = false);

/// <summary>
/// Declares the broker layout idempotently:
/// <list type="bullet">
/// <item><c>districtsync.events</c> (topic): all integration events, routed by wire name.</item>
/// <item><c>districtsync.retry.{5s,30s,2m}</c> (fanout) → TTL queues that dead-letter back to the default exchange,
/// so an expired message lands in the queue named by its routing key (= the original work queue). This is
/// delayed redelivery without the delayed-message plugin.</item>
/// <item><c>districtsync.dlx</c> → <c>districtsync.dead-letter</c>: terminal parking for messages that exhausted retries.</item>
/// </list>
/// </summary>
public static class RabbitMqTopology
{
    public static readonly IReadOnlyList<(string Tier, TimeSpan Delay)> RetryTiers =
    [
        ("5s", TimeSpan.FromSeconds(5)),
        ("30s", TimeSpan.FromSeconds(30)),
        ("2m", TimeSpan.FromMinutes(2)),
    ];

    public static string RetryExchange(string tier) => $"districtsync.retry.{tier}";

    public static string RetryQueue(string tier) => $"districtsync.retry.{tier}";

    public static string TierForAttempt(int attempt) => RetryTiers[Math.Clamp(attempt - 1, 0, RetryTiers.Count - 1)].Tier;

    public static async Task EnsureCoreAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(EventCatalog.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(EventCatalog.DeadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(EventCatalog.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(EventCatalog.DeadLetterQueue, EventCatalog.DeadLetterExchange, string.Empty, cancellationToken: ct);

        foreach (var (tier, delay) in RetryTiers)
        {
            await channel.ExchangeDeclareAsync(RetryExchange(tier), ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: ct);
            await channel.QueueDeclareAsync(RetryQueue(tier), durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object?>
            {
                ["x-message-ttl"] = (int)delay.TotalMilliseconds,
                ["x-dead-letter-exchange"] = string.Empty,
            }, cancellationToken: ct);
            await channel.QueueBindAsync(RetryQueue(tier), RetryExchange(tier), string.Empty, cancellationToken: ct);
        }
    }

    public static async Task EnsureSubscriptionAsync(IChannel channel, QueueSubscription subscription, CancellationToken ct)
    {
        await channel.QueueDeclareAsync(subscription.QueueName, durable: !subscription.Exclusive, exclusive: subscription.Exclusive, autoDelete: subscription.Exclusive, arguments: new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = EventCatalog.DeadLetterExchange,
            ["x-queue-type"] = "classic",
        }, cancellationToken: ct);

        foreach (var key in subscription.RoutingKeys)
        {
            await channel.QueueBindAsync(subscription.QueueName, EventCatalog.Exchange, key, cancellationToken: ct);
        }
    }
}
