using System.Collections.Frozen;

namespace DistrictSync.Contracts.Events;

/// <summary>
/// Single source of truth for the wire name (RabbitMQ routing key) of every integration event.
/// The wire name is stored on outbox rows and in the AMQP <c>type</c> property; consumers use it to
/// pick the CLR type to deserialize into. Keeping the mapping explicit (rather than using CLR type
/// names) means events can be renamed or moved without breaking messages already in flight.
/// </summary>
public static class EventCatalog
{
    public const string Exchange = "districtsync.events";
    public const string DeadLetterExchange = "districtsync.dlx";
    public const string DeadLetterQueue = "districtsync.dead-letter";

    private static readonly FrozenDictionary<Type, string> NamesByType = new Dictionary<Type, string>
    {
        [typeof(IngestionBatchReceivedEvent)] = "ingestion.batch-received",
        [typeof(IngestionProgressEvent)] = "ingestion.progress",
        [typeof(IngestionBatchCompletedEvent)] = "ingestion.batch-completed",
        [typeof(IngestionBatchFailedEvent)] = "ingestion.batch-failed",
        [typeof(ReconciliationRequestedEvent)] = "reconciliation.requested",
        [typeof(ReconciliationProgressEvent)] = "reconciliation.progress",
        [typeof(ReconciliationCompletedEvent)] = "reconciliation.completed",
        [typeof(ReconciliationFailedEvent)] = "reconciliation.failed",
        [typeof(RuleSetPublishedEvent)] = "rules.published",
        [typeof(CaseOpenedEvent)] = "cases.opened",
        [typeof(CaseStateChangedEvent)] = "cases.state-changed",
        [typeof(CaseApprovedEvent)] = "cases.approved",
        [typeof(CaseEscalatedEvent)] = "cases.escalated",
        [typeof(MessageDeadLetteredEvent)] = "operations.dead-lettered",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, Type> TypesByName = NamesByType.ToFrozenDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    public static string NameOf<TEvent>() where TEvent : IIntegrationEvent => NameOf(typeof(TEvent));

    public static string NameOf(Type eventType) => NamesByType.TryGetValue(eventType, out var name)
        ? name
        : throw new InvalidOperationException($"{eventType.Name} is not registered in the EventCatalog.");

    public static Type? Resolve(string wireName) => TypesByName.TryGetValue(wireName, out var type) ? type : null;

    public static IReadOnlyCollection<string> AllNames => TypesByName.Keys;

    /// <summary>Events that only matter for live dashboards: not business-critical, so they bypass the outbox.</summary>
    public static bool IsEphemeral(Type eventType) => eventType == typeof(IngestionProgressEvent) || eventType == typeof(ReconciliationProgressEvent);
}
