using DistrictSync.Domain.Common;

namespace DistrictSync.Application.Messaging;

/// <summary>A message that exhausted its retries, parked in the database so operators can inspect and replay it.</summary>
public sealed class DeadLetterMessage
{
    private DeadLetterMessage() { }

    public Guid Id { get; private set; }
    public Guid? MessageId { get; private set; }
    public TenantId? TenantId { get; private set; }
    public string Queue { get; private set; } = string.Empty;
    public string RoutingKey { get; private set; } = string.Empty;
    public string MessageType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public int Attempts { get; private set; }
    public DateTimeOffset DeadLetteredAt { get; private set; }
    public DateTimeOffset? ReplayedAt { get; private set; }
    public string? ReplayedBy { get; private set; }

    public static DeadLetterMessage Park(Guid? messageId, TenantId? tenantId, string queue, string routingKey, string messageType, string payload, string reason, int attempts, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        MessageId = messageId,
        TenantId = tenantId,
        Queue = queue,
        RoutingKey = routingKey,
        MessageType = messageType,
        Payload = payload,
        Reason = reason.Length > 4000 ? reason[..4000] : reason,
        Attempts = attempts,
        DeadLetteredAt = now,
    };

    public void MarkReplayed(string by, DateTimeOffset now)
    {
        ReplayedAt = now;
        ReplayedBy = by;
    }
}
