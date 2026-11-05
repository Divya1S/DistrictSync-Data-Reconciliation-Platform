using DistrictSync.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DistrictSync.Api.Hubs;

/// <summary>
/// Live operations feed. Connections join their tenant's group on connect, so a broadcast to
/// <c>tenant:{id}</c> reaches exactly that tenant's users (across all API instances via the Redis backplane).
/// </summary>
[Authorize]
public sealed class OperationsHub : Hub
{
    public const string LiveUpdateMethod = "liveUpdate";

    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId:D}";

    public override async Task OnConnectedAsync()
    {
        var tenant = Context.User?.FindFirst(ClaimNames.TenantId)?.Value;
        if (Guid.TryParse(tenant, out var tenantId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));
        }

        await base.OnConnectedAsync();
    }
}
