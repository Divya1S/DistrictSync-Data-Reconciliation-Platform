using DistrictSync.Domain.Common;

namespace DistrictSync.Application.Messaging;

/// <summary>
/// Transactional outbox row. Written in the same transaction as the business change; a dispatcher
/// relays it to RabbitMQ afterwards. At-least-once delivery; consumers de-duplicate via the inbox.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage() { }

    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;

    /// <summary>W3C traceparent captured at enqueue time so the consumer span joins the producer's trace.</summary>
    public string? TraceParent { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>Claim lease: lets several dispatcher instances share the table without double-publishing.</summary>
    public string? LockOwner { get; private set; }
    public DateTimeOffset? LockExpiresAt { get; private set; }

    public static OutboxMessage Create(Guid id, TenantId tenantId, string type, string payload, string? traceParent, DateTimeOffset occurredAt) => new()
    {
        Id = id,
        TenantId = tenantId,
        Type = type,
        Payload = payload,
        TraceParent = traceParent,
        OccurredAt = occurredAt,
    };

    public void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAt = now;
        LockOwner = null;
        LockExpiresAt = null;
    }

    public void MarkFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
        NextAttemptAt = now + TimeSpan.FromSeconds(Math.Min(Math.Pow(2, Attempts), 300));
        LockOwner = null;
        LockExpiresAt = null;
    }
}
