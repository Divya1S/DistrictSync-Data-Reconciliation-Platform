using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DistrictSync.Infrastructure.Messaging;

/// <summary>Owns one channel per subscribed queue and feeds deliveries to the <see cref="MessagePipeline"/>.</summary>
public sealed class RabbitMqConsumerHost(IConnection connection, MessagePipeline pipeline, IEnumerable<QueueSubscription> subscriptions, IOptions<MessagingOptions> options, ILogger<RabbitMqConsumerHost> logger) : BackgroundService
{
    private readonly List<IChannel> _channels = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subs = subscriptions.ToList();
        if (subs.Count == 0)
        {
            return;
        }

        using (var setup = await connection.CreateChannelAsync(cancellationToken: stoppingToken))
        {
            await RabbitMqTopology.EnsureCoreAsync(setup, stoppingToken);
            foreach (var sub in subs)
            {
                await RabbitMqTopology.EnsureSubscriptionAsync(setup, sub, stoppingToken);
            }
        }

        foreach (var sub in subs)
        {
            var channel = await connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: false, publisherConfirmationTrackingEnabled: false, consumerDispatchConcurrency: options.Value.Concurrency), stoppingToken);
            await channel.BasicQosAsync(0, options.Value.Prefetch, global: false, stoppingToken);
            _channels.Add(channel);

            var consumer = new AsyncEventingBasicConsumer(channel);
            var queueName = sub.QueueName;
            consumer.ReceivedAsync += async (_, ea) =>
            {
                var verdict = await pipeline.HandleAsync(queueName, ea, stoppingToken);
                try
                {
                    switch (verdict)
                    {
                        case DeliveryVerdict.Ack:
                            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, CancellationToken.None);
                            break;
                        case DeliveryVerdict.DeadLetter:
                            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
                            break;
                        default:
                            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to settle delivery {DeliveryTag} on {Queue}; the broker will redeliver", ea.DeliveryTag, queueName);
                }
            };

            await channel.BasicConsumeAsync(queueName, autoAck: false, consumer, stoppingToken);
            logger.LogInformation("Consuming {Queue} ← [{Keys}] (prefetch {Prefetch}, concurrency {Concurrency})", queueName, string.Join(", ", sub.RoutingKeys), options.Value.Prefetch, options.Value.Concurrency);
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        foreach (var channel in _channels)
        {
            try
            {
                await channel.CloseAsync(cancellationToken: cancellationToken);
                await channel.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Ignoring channel close error during shutdown");
            }
        }
    }
}
