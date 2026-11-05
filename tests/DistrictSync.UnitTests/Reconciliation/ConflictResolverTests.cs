using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Reconciliation;
using DistrictSync.Application.Rules;
using DistrictSync.Domain.Common;
using DistrictSync.Domain.Reconciliation;
using DistrictSync.Domain.Rules;
using DistrictSync.Domain.Sources;

namespace DistrictSync.UnitTests.Reconciliation;

public class ConflictResolverTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);
    private static readonly SourceSystem Hr = SourceSystem.Create(Tenant, "HR", "HR", SourceSystemKind.CsvUpload, 90, "{}", Now);
    private static readonly SourceSystem Payroll = SourceSystem.Create(Tenant, "Payroll", "PAYROLL", SourceSystemKind.CsvUpload, 70, "{}", Now);

    private static DiscrepancyContext Conflict(DateTimeOffset? leftUpdated, DateTimeOffset? rightUpdated, SourceSystem? right = null)
    {
        right ??= Payroll;
        var left = new KeyedRecord(Guid.NewGuid(), "k", "L1", """{"title":"Teacher","phone":"1"}""", leftUpdated);
        var r = new KeyedRecord(Guid.NewGuid(), "k", "R1", """{"title":"Lead Teacher","phone":"1"}""", rightUpdated);
        var d = Discrepancy.Detect(Tenant, Guid.NewGuid(), "Employee", DiscrepancyType.FieldConflict, "k", Hr.Id, right.Id, left.Id, r.Id, [new FieldDifference("title", "Teacher", "Lead Teacher", false)], Now);
        return new DiscrepancyContext(d, Hr, right, left, r);
    }

    [Fact]
    public void PreferNewest_picks_the_side_with_the_later_timestamp()
    {
        var ctx = Conflict(Now.AddDays(-2), Now.AddDays(-1));
        var outcome = ConflictResolver.Resolve(ResolutionStrategy.PreferNewest, ctx);
        outcome.Winner.ShouldBe(WinningSide.Right);
        outcome.Action.ShouldBe(WriteBackAction.UpdateFields);
        outcome.TargetSourceSystemId.ShouldBe(Hr.Id);
        outcome.PayloadJson.ShouldContain("Lead Teacher");
    }

    [Fact]
    public void PreferNewest_falls_back_to_trust_on_equal_timestamps()
    {
        var ctx = Conflict(Now, Now);
        ConflictResolver.PickWinner(ResolutionStrategy.PreferNewest, ctx).ShouldBe(WinningSide.Left);
    }

    [Fact]
    public void PreferNewest_is_undecidable_without_timestamps()
    {
        var outcome = ConflictResolver.Resolve(ResolutionStrategy.PreferNewest, Conflict(null, null));
        outcome.Action.ShouldBe(WriteBackAction.NoOp);
        outcome.Winner.ShouldBe(WinningSide.None);
    }

    [Fact]
    public void PreferHigherTrust_is_undecidable_on_equal_trust()
    {
        var sameTrust = SourceSystem.Create(Tenant, "Other", "OTHER", SourceSystemKind.Webhook, 90, "{}", Now);
        ConflictResolver.Resolve(ResolutionStrategy.PreferHigherTrust, Conflict(null, null, sameTrust)).Action.ShouldBe(WriteBackAction.NoOp);
    }

    [Fact]
    public void Missing_record_is_created_in_the_non_authoritative_system()
    {
        var left = new KeyedRecord(Guid.NewGuid(), "k", "L1", """{"title":"Teacher"}""", Now);
        var d = Discrepancy.Detect(Tenant, Guid.NewGuid(), "Employee", DiscrepancyType.MissingInRight, "k", Hr.Id, Payroll.Id, left.Id, null, [], Now);
        var outcome = ConflictResolver.Resolve(ResolutionStrategy.PreferHigherTrust, new DiscrepancyContext(d, Hr, Payroll, left, null));

        outcome.Action.ShouldBe(WriteBackAction.CreateRecord);
        outcome.TargetSourceSystemId.ShouldBe(Payroll.Id);
    }

    [Fact]
    public void Missing_record_is_deleted_when_the_system_lacking_it_is_authoritative()
    {
        var right = new KeyedRecord(Guid.NewGuid(), "k", "R1", """{"title":"Teacher"}""", Now);
        var d = Discrepancy.Detect(Tenant, Guid.NewGuid(), "Employee", DiscrepancyType.MissingInLeft, "k", Hr.Id, Payroll.Id, null, right.Id, [], Now);
        var outcome = ConflictResolver.Resolve(ResolutionStrategy.PreferHigherTrust, new DiscrepancyContext(d, Hr, Payroll, null, right));

        outcome.Action.ShouldBe(WriteBackAction.DeleteRecord);
        outcome.TargetSourceSystemId.ShouldBe(Payroll.Id);
    }

    [Fact]
    public void Duplicates_are_never_auto_resolved()
    {
        var rec = new KeyedRecord(Guid.NewGuid(), "k", "L1", "{}", Now);
        var d = Discrepancy.Detect(Tenant, Guid.NewGuid(), "Employee", DiscrepancyType.Duplicate, "k", Hr.Id, Payroll.Id, rec.Id, null, [], Now);
        ConflictResolver.Resolve(ResolutionStrategy.PreferLeft, new DiscrepancyContext(d, Hr, Payroll, rec, null)).Action.ShouldBe(WriteBackAction.NoOp);
    }
}
