namespace DistrictSync.Domain.Common;

/// <summary>Raised when an operation would violate a domain invariant. Maps to HTTP 422 at the API boundary.</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Raised when a state machine receives a transition that is not legal from its current state. Maps to HTTP 409.</summary>
public sealed class InvalidStateTransitionException(string entity, string from, string action)
    : DomainException("invalid_state_transition", $"{entity} cannot '{action}' while in state '{from}'.")
{
    public string Entity { get; } = entity;
    public string From { get; } = from;
    public string Action { get; } = action;
}

/// <summary>Raised when a cross-tenant access is attempted. Must never be surfaced with details to the caller.</summary>
public sealed class TenantIsolationViolationException(string message) : DomainException("tenant_isolation_violation", message);
