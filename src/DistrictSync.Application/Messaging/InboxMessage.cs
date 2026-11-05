namespace DistrictSync.Application.Messaging;

/// <summary>
/// Idempotency ledger for consumers. Primary key (MessageId, Consumer): a redelivered message cannot be
/// processed twice by the same consumer because the second insert violates the key. Long-running
/// handlers additionally hold a lease while they work, so a concurrent duplicate delivery is dropped
/// instead of starting the job a second time; an expired lease (worker crash) makes the job resumable.
/// </summary>
public sealed class InboxMessage
{
    private InboxMessage() { }

    public Guid MessageId { get; private set; }
    public string Consumer { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public DateTimeOffset FirstReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public int Attempts { get; private set; }

    public static InboxMessage Claim(Guid messageId, string consumer, string type, DateTimeOffset now, TimeSpan? lease = null) => new()
    {
        MessageId = messageId,
        Consumer = consumer,
        Type = type,
        FirstReceivedAt = now,
        LeaseExpiresAt = lease is null ? null : now + lease,
        Attempts = 1,
    };

    public bool IsProcessed => ProcessedAt is not null;

    public bool IsLeased(DateTimeOffset now) => !IsProcessed && LeaseExpiresAt is not null && LeaseExpiresAt > now;

    public void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAt = now;
        LeaseExpiresAt = null;
    }

    public void Reclaim(DateTimeOffset now, TimeSpan lease)
    {
        Attempts++;
        LeaseExpiresAt = now + lease;
    }

    public void ExtendLease(DateTimeOffset now, TimeSpan lease) => LeaseExpiresAt = now + lease;

    public void ReleaseLease() => LeaseExpiresAt = null;
}
