using DistrictSync.Application.Abstractions;
using DistrictSync.Contracts.Api;
using DistrictSync.Contracts.Events;
using Microsoft.AspNetCore.SignalR;

namespace DistrictSync.Api.Hubs;

/// <summary>Pushes an integration event to the owning tenant's SignalR group. No inbox: a duplicate push is harmless.</summary>
public sealed class LiveUpdateBroadcaster<TEvent>(IHubContext<OperationsHub> hub, TimeProvider clock) : IIntegrationEventHandler<TEvent> where TEvent : IIntegrationEvent
{
    public HandlerMode Mode => HandlerMode.NoInbox;

    public Task HandleAsync(TEvent integrationEvent, CancellationToken ct) =>
        hub.Clients.Group(OperationsHub.TenantGroup(integrationEvent.TenantId))
            .SendAsync(OperationsHub.LiveUpdateMethod, new LiveUpdate(EventCatalog.NameOf<TEvent>(), clock.GetUtcNow(), integrationEvent), ct);
}

public static class LiveUpdateRegistration
{
    public static readonly Type[] DashboardEvents =
    [
        typeof(IngestionProgressEvent),
        typeof(IngestionBatchCompletedEvent),
        typeof(IngestionBatchFailedEvent),
        typeof(ReconciliationProgressEvent),
        typeof(ReconciliationCompletedEvent),
        typeof(ReconciliationFailedEvent),
        typeof(CaseOpenedEvent),
        typeof(CaseStateChangedEvent),
        typeof(CaseEscalatedEvent),
        typeof(MessageDeadLetteredEvent),
    ];

    public static IServiceCollection AddLiveUpdateBroadcasters(this IServiceCollection services)
    {
        foreach (var eventType in DashboardEvents)
        {
            services.AddScoped(typeof(IIntegrationEventHandler<>).MakeGenericType(eventType), typeof(LiveUpdateBroadcaster<>).MakeGenericType(eventType));
        }

        return services;
    }

    public static string[] RoutingKeys => DashboardEvents.Select(EventCatalog.NameOf).ToArray();
}
