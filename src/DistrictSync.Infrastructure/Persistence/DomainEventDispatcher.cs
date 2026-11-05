using DistrictSync.Application.Abstractions;
using DistrictSync.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DistrictSync.Infrastructure.Persistence;

/// <summary>
/// Drains domain events from tracked aggregates and invokes their handlers before the change set is
/// saved. Handlers may raise further changes (or more events), so the loop runs until quiescent.
/// </summary>
public sealed class DomainEventDispatcher(IServiceProvider services)
{
    private const int MaxRounds = 10;

    public async Task DispatchAsync(DbContext context, CancellationToken ct)
    {
        for (var round = 0; round < MaxRounds; round++)
        {
            var aggregates = context.ChangeTracker.Entries<AggregateRoot>()
                .Select(e => e.Entity)
                .Where(a => a.DomainEvents.Count > 0)
                .ToList();

            if (aggregates.Count == 0)
            {
                return;
            }

            var events = aggregates.SelectMany(a => a.DomainEvents).ToList();
            foreach (var aggregate in aggregates)
            {
                aggregate.ClearDomainEvents();
            }

            foreach (var domainEvent in events)
            {
                var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
                foreach (var handler in services.GetServices(handlerType))
                {
                    var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
                    await (Task)method.Invoke(handler, [domainEvent, ct])!;
                }
            }
        }

        throw new InvalidOperationException($"Domain event dispatch did not settle after {MaxRounds} rounds; check for handlers that raise events in a cycle.");
    }
}
