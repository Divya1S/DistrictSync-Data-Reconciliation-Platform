using System.Diagnostics;
using System.Text.Json;
using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Common;
using DistrictSync.Application.Messaging;
using DistrictSync.Contracts.Events;
using DistrictSync.Domain.Common;

namespace DistrictSync.Infrastructure.Persistence;

/// <summary>Scoped buffer of integration events waiting to become outbox rows at SaveChanges time.</summary>
public sealed class OutboxWriter : IOutbox
{
    private readonly List<IIntegrationEvent> _pending = [];

    public void Enqueue(IIntegrationEvent integrationEvent) => _pending.Add(integrationEvent);

    public int PendingCount => _pending.Count;

    public IEnumerable<OutboxMessage> Drain(DateTimeOffset now)
    {
        if (_pending.Count == 0)
        {
            yield break;
        }

        var traceParent = Activity.Current?.Id;
        var snapshot = _pending.ToArray();
        _pending.Clear();
        foreach (var e in snapshot)
        {
            var type = e.GetType();
            yield return OutboxMessage.Create(
                e.EventId,
                new TenantId(e.TenantId),
                EventCatalog.NameOf(type),
                JsonSerializer.Serialize(e, type, JsonDefaults.Options),
                traceParent,
                e.OccurredAt == default ? now : e.OccurredAt);
        }
    }
}
