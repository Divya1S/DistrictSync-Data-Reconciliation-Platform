namespace DistrictSync.Domain.Common;

/// <summary>
/// Something that happened inside the domain. Domain events are dispatched in-process when the unit of
/// work is saved; handlers typically translate them into integration events written to the outbox in
/// the same transaction, which is what makes publication to RabbitMQ atomic with the state change.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
