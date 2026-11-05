using DistrictSync.Domain.Common;

namespace DistrictSync.Application.Messaging;

/// <summary>Allocates event identity consistently (v7 GUIDs sort by time, which keeps outbox/inbox indexes append-friendly).</summary>
public static class EventIds
{
    public static Guid Next() => Guid.CreateVersion7();

    public static Guid Of(TenantId tenantId) => tenantId.Value;
}
