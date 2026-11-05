using DistrictSync.Domain.Cases;
using DistrictSync.Domain.Common;
using DistrictSync.Domain.Rules;

namespace DistrictSync.UnitTests.Domain;

public class ResolutionCaseTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private static ResolutionCase Open(CasePriority priority = CasePriority.High) =>
        ResolutionCase.Open(TenantId.New(), Guid.NewGuid(), Guid.NewGuid(), "Salary differs", priority, "payroll", Guid.NewGuid(), T0);

    [Fact]
    public void Opening_sets_due_date_from_sla_and_raises_event()
    {
        var c = Open();
        c.State.ShouldBe(CaseState.Open);
        c.DueAt.ShouldBe(T0 + SlaPolicy.For(CasePriority.High).InitialDue);
        c.DomainEvents.OfType<CaseOpened>().ShouldHaveSingleItem();
        c.History.ShouldHaveSingleItem().Action.ShouldBe("open");
    }

    [Fact]
    public void Happy_path_assign_propose_approve_resolve()
    {
        var c = Open();
        c.Assign("alice", "manager", T0.AddMinutes(1));
        c.State.ShouldBe(CaseState.Assigned);
        c.Propose(ResolutionStrategy.PreferHigherTrust, null, "HR is right", "alice", T0.AddMinutes(2));
        c.State.ShouldBe(CaseState.PendingApproval);
        c.Approve("bob", T0.AddMinutes(3));
        c.State.ShouldBe(CaseState.Approved);
        c.ApprovedBy.ShouldBe("bob");
        c.DomainEvents.OfType<CaseApproved>().ShouldHaveSingleItem();
        c.MarkResolved("applied", T0.AddMinutes(4));
        c.State.ShouldBe(CaseState.Resolved);
        c.IsTerminal.ShouldBeTrue();
        c.ClosedAt.ShouldBe(T0.AddMinutes(4));
        c.History.Select(h => h.Action).ShouldBe(["open", "assign", "propose", "approve", "resolve"]);
    }

    [Fact]
    public void Proposer_cannot_approve_their_own_proposal()
    {
        var c = Open();
        c.Assign("alice", "alice", T0);
        c.Propose(ResolutionStrategy.PreferLeft, null, "note", "alice", T0);
        Should.Throw<DomainException>(() => c.Approve("ALICE", T0)).Code.ShouldBe("self_approval");
    }

    [Fact]
    public void Illegal_transitions_throw()
    {
        var c = Open();
        Should.Throw<InvalidStateTransitionException>(() => c.Approve("bob", T0));
        Should.Throw<InvalidStateTransitionException>(() => c.Propose(ResolutionStrategy.PreferLeft, null, "n", "a", T0));
        Should.Throw<InvalidStateTransitionException>(() => c.MarkResolved("x", T0));
        c.Cancel("dup", "alice", T0);
        Should.Throw<InvalidStateTransitionException>(() => c.Assign("x", "y", T0));
        Should.Throw<InvalidStateTransitionException>(() => c.Cancel("again", "alice", T0));
    }

    [Fact]
    public void Reject_returns_case_to_assignee()
    {
        var c = Open();
        c.Assign("alice", "m", T0);
        c.Propose(null, """{"salary":"1"}""", "manual", "alice", T0);
        c.Reject("wrong value", "bob", T0);
        c.State.ShouldBe(CaseState.Assigned);
        c.Proposal.ShouldBeNull();
    }

    [Fact]
    public void Proposal_requires_strategy_or_values()
    {
        var c = Open();
        c.Assign("alice", "m", T0);
        Should.Throw<DomainException>(() => c.Propose(null, null, "nothing", "alice", T0)).Code.ShouldBe("empty_proposal");
    }

    [Fact]
    public void Escalation_extends_due_date_until_the_policy_ceiling()
    {
        var c = Open(CasePriority.Normal); // 2 escalations max
        var sla = SlaPolicy.For(CasePriority.Normal);

        c.Escalate("late", c.DueAt.AddMinutes(1)).ShouldBeTrue();
        c.State.ShouldBe(CaseState.Escalated);
        c.EscalationLevel.ShouldBe(1);
        c.DueAt.ShouldBe(T0 + sla.InitialDue + TimeSpan.FromMinutes(1) + sla.EscalationStep);

        c.Escalate("later", c.DueAt.AddMinutes(1)).ShouldBeTrue();
        c.EscalationLevel.ShouldBe(2);

        c.Escalate("latest", c.DueAt.AddMinutes(1)).ShouldBeFalse();
        c.EscalationLevel.ShouldBe(2);
        c.DomainEvents.OfType<CaseEscalated>().Count().ShouldBe(2);
    }

    [Fact]
    public void Escalated_case_can_be_assigned_and_worked()
    {
        var c = Open();
        c.Escalate("late", c.DueAt.AddHours(1));
        c.Assign("carol", "system", T0);
        c.State.ShouldBe(CaseState.Assigned);
    }

    [Fact]
    public void Terminal_and_approved_cases_do_not_escalate()
    {
        var c = Open();
        c.Assign("a", "m", T0);
        c.Propose(ResolutionStrategy.PreferLeft, null, "n", "a", T0);
        c.Approve("b", T0);
        c.Escalate("late", T0.AddDays(10)).ShouldBeFalse();
    }
}
