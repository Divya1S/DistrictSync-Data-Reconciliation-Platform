using DistrictSync.Domain.Common;
using DistrictSync.Domain.Workflows;

namespace DistrictSync.UnitTests.Domain;

public class WorkflowInstanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Claimable_only_when_due_and_unleased()
    {
        var w = WorkflowInstance.Schedule(TenantId.New(), "case-sla", Guid.NewGuid(), "{}", Now.AddHours(1), Now);
        w.IsClaimable(Now).ShouldBeFalse();
        w.IsClaimable(Now.AddHours(1)).ShouldBeTrue();

        w.Claim("worker-a", TimeSpan.FromSeconds(30), Now.AddHours(1));
        w.Status.ShouldBe(WorkflowStatus.Running);
        w.Attempts.ShouldBe(1);
        w.IsClaimable(Now.AddHours(1).AddSeconds(10)).ShouldBeFalse("lease still held");
        w.IsClaimable(Now.AddHours(1).AddSeconds(31)).ShouldBeTrue("lease expired → crash recovery");
        Should.Throw<InvalidStateTransitionException>(() => w.Claim("worker-b", TimeSpan.FromSeconds(30), Now.AddHours(1).AddSeconds(5)));
    }

    [Fact]
    public void Sleep_releases_lease_and_resets_attempts()
    {
        var w = WorkflowInstance.Schedule(TenantId.New(), "t", Guid.NewGuid(), "{}", Now, Now);
        w.Claim("a", TimeSpan.FromSeconds(30), Now);
        w.Sleep("""{"step":2}""", Now.AddDays(1), Now.AddSeconds(1));
        w.Status.ShouldBe(WorkflowStatus.Scheduled);
        w.LockOwner.ShouldBeNull();
        w.Attempts.ShouldBe(0);
        w.WakeUpAt.ShouldBe(Now.AddDays(1));
        w.StateJson.ShouldBe("""{"step":2}""");
    }

    [Fact]
    public void Failures_back_off_exponentially_then_fail_permanently()
    {
        var w = WorkflowInstance.Schedule(TenantId.New(), "t", Guid.NewGuid(), "{}", Now, Now);
        var t = Now;
        for (var attempt = 1; attempt < WorkflowInstance.MaxAttempts; attempt++)
        {
            w.Claim("a", TimeSpan.FromSeconds(30), t);
            w.RecordFailure("boom", t).ShouldBeTrue();
            w.Status.ShouldBe(WorkflowStatus.Scheduled);
            (w.WakeUpAt - t).ShouldBe(TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), 900)));
            t = w.WakeUpAt;
        }

        w.Claim("a", TimeSpan.FromSeconds(30), t);
        w.RecordFailure("boom", t).ShouldBeFalse();
        w.Status.ShouldBe(WorkflowStatus.Failed);
        w.LastError.ShouldBe("boom");
    }

    [Fact]
    public void Nudge_only_moves_future_scheduled_wakeups_earlier()
    {
        var w = WorkflowInstance.Schedule(TenantId.New(), "t", Guid.NewGuid(), "{}", Now.AddDays(1), Now);
        w.Nudge(Now.AddHours(1));
        w.WakeUpAt.ShouldBe(Now.AddHours(1));
        w.Claim("a", TimeSpan.FromSeconds(30), Now.AddHours(1));
        w.Nudge(Now.AddHours(2));
        w.Status.ShouldBe(WorkflowStatus.Running);
    }
}
