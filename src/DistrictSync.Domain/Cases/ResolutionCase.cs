using DistrictSync.Domain.Common;
using DistrictSync.Domain.Rules;

namespace DistrictSync.Domain.Cases;

public enum CaseState
{
    Open = 1,
    Assigned = 2,
    PendingApproval = 3,
    Approved = 4,
    Resolved = 5,
    Escalated = 6,
    Cancelled = 7,
}

/// <summary>The outcome a human proposes for a discrepancy, awaiting approval.</summary>
public sealed record ProposedResolution(ResolutionStrategy? Strategy, string? ManualValuesJson, string Note, string ProposedBy, DateTimeOffset ProposedAt);

/// <summary>Append-only audit trail of everything that happened to a case.</summary>
public sealed class CaseHistoryEntry
{
    private CaseHistoryEntry() { }

    public long Id { get; private set; }
    public Guid CaseId { get; private set; }
    public DateTimeOffset At { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string? FromState { get; private set; }
    public string ToState { get; private set; } = string.Empty;
    public string? Note { get; private set; }

    internal static CaseHistoryEntry Create(Guid caseId, DateTimeOffset at, string actor, string action, CaseState? from, CaseState to, string? note) => new()
    {
        CaseId = caseId,
        At = at,
        Actor = Guard.MaxLength(actor, 200),
        Action = action,
        FromState = from?.ToString(),
        ToState = to.ToString(),
        Note = note is null ? null : Guard.MaxLength(note, 2000),
    };
}

/// <summary>
/// A human-in-the-loop exception. The case is a state machine: every transition is validated here, so
/// neither the API nor the workflow engine can put a case into an illegal state. SLA escalation is
/// driven by a durable <c>WorkflowInstance</c> that wakes up at <see cref="DueAt"/>.
/// </summary>
public sealed class ResolutionCase : AggregateRoot, ITenantScoped
{
    private readonly List<CaseHistoryEntry> _history = [];

    private ResolutionCase() { }

    public TenantId TenantId { get; private set; }
    public Guid DiscrepancyId { get; private set; }
    public Guid RunId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public CasePriority Priority { get; private set; }
    public CaseState State { get; private set; }
    public string? AssignedRole { get; private set; }
    public string? AssigneeId { get; private set; }
    public int EscalationLevel { get; private set; }
    public DateTimeOffset DueAt { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public ProposedResolution? Proposal { get; private set; }
    public string? ApprovedBy { get; private set; }
    public Guid? OpenedByRuleId { get; private set; }
    public IReadOnlyCollection<CaseHistoryEntry> History => _history.AsReadOnly();

    public bool IsTerminal => State is CaseState.Resolved or CaseState.Cancelled;

    public static ResolutionCase Open(
        TenantId tenantId,
        Guid discrepancyId,
        Guid runId,
        string title,
        CasePriority priority,
        string? assignedRole,
        Guid? openedByRuleId,
        DateTimeOffset now)
    {
        var sla = SlaPolicy.For(priority);
        var c = new ResolutionCase
        {
            TenantId = tenantId,
            DiscrepancyId = Guard.NotEmpty(discrepancyId),
            RunId = runId,
            Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title), 300),
            Priority = priority,
            State = CaseState.Open,
            AssignedRole = assignedRole is null ? null : Guard.MaxLength(assignedRole, 64),
            OpenedByRuleId = openedByRuleId,
            OpenedAt = now,
            DueAt = now + sla.InitialDue,
        };
        c.Record(now, "system", "open", null, CaseState.Open, openedByRuleId is null ? null : $"Opened by rule {openedByRuleId:D}");
        c.Raise(new CaseOpened(tenantId, c.Id, discrepancyId, runId, priority, assignedRole, c.DueAt, now));
        return c;
    }

    public void Assign(string assigneeId, string actor, DateTimeOffset now)
    {
        EnsureState(nameof(Assign), CaseState.Open, CaseState.Assigned, CaseState.Escalated);
        var from = State;
        AssigneeId = Guard.MaxLength(Guard.NotNullOrWhiteSpace(assigneeId), 200);
        State = CaseState.Assigned;
        Record(now, actor, "assign", from, State, $"Assigned to {assigneeId}");
        Raise(new CaseStateChanged(TenantId, Id, from, State, actor, now));
    }

    public void Propose(ResolutionStrategy? strategy, string? manualValuesJson, string note, string actor, DateTimeOffset now)
    {
        EnsureState(nameof(Propose), CaseState.Assigned, CaseState.Escalated);
        if (strategy is null && string.IsNullOrWhiteSpace(manualValuesJson))
        {
            throw new DomainException("empty_proposal", "A proposal needs either a strategy or manual values.");
        }

        var from = State;
        Proposal = new ProposedResolution(strategy, manualValuesJson, Guard.MaxLength(Guard.NotNullOrWhiteSpace(note), 2000), actor, now);
        State = CaseState.PendingApproval;
        Record(now, actor, "propose", from, State, note);
        Raise(new CaseStateChanged(TenantId, Id, from, State, actor, now));
    }

    public void Approve(string actor, DateTimeOffset now)
    {
        EnsureState(nameof(Approve), CaseState.PendingApproval);
        if (Proposal is not null && string.Equals(Proposal.ProposedBy, actor, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("self_approval", "A proposal cannot be approved by the person who made it.");
        }

        var from = State;
        ApprovedBy = Guard.MaxLength(actor, 200);
        State = CaseState.Approved;
        Record(now, actor, "approve", from, State, null);
        Raise(new CaseStateChanged(TenantId, Id, from, State, actor, now));
        Raise(new CaseApproved(TenantId, Id, DiscrepancyId, Proposal!, actor, now));
    }

    public void Reject(string reason, string actor, DateTimeOffset now)
    {
        EnsureState(nameof(Reject), CaseState.PendingApproval);
        var from = State;
        Proposal = null;
        State = AssigneeId is null ? CaseState.Open : CaseState.Assigned;
        Record(now, actor, "reject", from, State, Guard.MaxLength(Guard.NotNullOrWhiteSpace(reason), 2000));
        Raise(new CaseStateChanged(TenantId, Id, from, State, actor, now));
    }

    /// <summary>Called by the workflow engine once the approved resolution has been applied to the data.</summary>
    public void MarkResolved(string note, DateTimeOffset now)
    {
        EnsureState(nameof(MarkResolved), CaseState.Approved);
        var from = State;
        State = CaseState.Resolved;
        ClosedAt = now;
        Record(now, "system", "resolve", from, State, note);
        Raise(new CaseStateChanged(TenantId, Id, from, State, "system", now));
        Raise(new CaseResolved(TenantId, Id, DiscrepancyId, now));
    }

    /// <summary>
    /// SLA breach handling. Returns false when the case is terminal or already at its maximum escalation
    /// level, so the caller knows whether another timer should be scheduled.
    /// </summary>
    public bool Escalate(string reason, DateTimeOffset now)
    {
        if (IsTerminal || State == CaseState.Approved)
        {
            return false;
        }

        var sla = SlaPolicy.For(Priority);
        if (EscalationLevel >= sla.MaxEscalations)
        {
            return false;
        }

        var from = State;
        EscalationLevel++;
        DueAt = now + sla.EscalationStep;
        State = CaseState.Escalated;
        Record(now, "system", "escalate", from, State, reason);
        Raise(new CaseStateChanged(TenantId, Id, from, State, "system", now));
        Raise(new CaseEscalated(TenantId, Id, EscalationLevel, DueAt, now));
        return true;
    }

    public void Cancel(string reason, string actor, DateTimeOffset now)
    {
        if (IsTerminal)
        {
            throw new InvalidStateTransitionException(nameof(ResolutionCase), State.ToString(), nameof(Cancel));
        }

        var from = State;
        State = CaseState.Cancelled;
        ClosedAt = now;
        Record(now, actor, "cancel", from, State, Guard.MaxLength(Guard.NotNullOrWhiteSpace(reason), 2000));
        Raise(new CaseStateChanged(TenantId, Id, from, State, actor, now));
    }

    public void Comment(string note, string actor, DateTimeOffset now) => Record(now, actor, "comment", State, State, Guard.NotNullOrWhiteSpace(note));

    private void Record(DateTimeOffset at, string actor, string action, CaseState? from, CaseState to, string? note) => _history.Add(CaseHistoryEntry.Create(Id, at, actor, action, from, to, note));

    private void EnsureState(string action, params CaseState[] allowed)
    {
        if (!allowed.Contains(State))
        {
            throw new InvalidStateTransitionException(nameof(ResolutionCase), State.ToString(), action);
        }
    }
}

public sealed record CaseOpened(TenantId TenantId, Guid CaseId, Guid DiscrepancyId, Guid RunId, CasePriority Priority, string? AssignedRole, DateTimeOffset DueAt, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record CaseStateChanged(TenantId TenantId, Guid CaseId, CaseState From, CaseState To, string Actor, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record CaseApproved(TenantId TenantId, Guid CaseId, Guid DiscrepancyId, ProposedResolution Proposal, string ApprovedBy, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record CaseEscalated(TenantId TenantId, Guid CaseId, int EscalationLevel, DateTimeOffset NewDueAt, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record CaseResolved(TenantId TenantId, Guid CaseId, Guid DiscrepancyId, DateTimeOffset OccurredAt) : IDomainEvent;
