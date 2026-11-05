using DistrictSync.Domain.Common;
using DistrictSync.Domain.Rules;

namespace DistrictSync.UnitTests.Domain;

public class RuleSetTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Draft_can_be_edited_and_publishing_freezes_it()
    {
        var set = RuleSet.CreateDraft(TenantId.New(), "policy", "Employee", Now);
        var r1 = set.AddRule("a", "true", RuleAction.Ignore());
        var r2 = set.AddRule("b", "false", RuleAction.OpenCase(CasePriority.Low));
        r2.Order.ShouldBeGreaterThan(r1.Order);
        set.Reorder([r2.Id, r1.Id]);
        set.Rules.Select(r => r.Name).ShouldBe(["b", "a"]);

        set.Publish("alice", "first", Now);
        set.Status.ShouldBe(RuleSetStatus.Published);
        set.DomainEvents.OfType<RuleSetPublished>().ShouldHaveSingleItem().Version.ShouldBe(1);

        Should.Throw<InvalidStateTransitionException>(() => set.AddRule("c", "true", RuleAction.Ignore()));
        Should.Throw<InvalidStateTransitionException>(() => set.RemoveRule(r1.Id));
        Should.Throw<InvalidStateTransitionException>(() => set.Publish("alice", null, Now));
    }

    [Fact]
    public void Cannot_publish_an_empty_set()
    {
        var set = RuleSet.CreateDraft(TenantId.New(), "policy", "*", Now);
        Should.Throw<DomainException>(() => set.Publish("alice", null, Now)).Code.ShouldBe("empty_ruleset");
    }

    [Fact]
    public void Next_version_copies_rules_into_a_new_draft_in_the_same_family()
    {
        var v1 = RuleSet.CreateDraft(TenantId.New(), "policy", "Employee", Now);
        v1.AddRule("a", "fields.any(\"x\")", RuleAction.AutoResolve(ResolutionStrategy.PreferNewest));
        v1.Publish("alice", null, Now);

        var v2 = v1.CreateNextVersion(Now.AddDays(1));
        v2.Id.ShouldNotBe(v1.Id);
        v2.FamilyId.ShouldBe(v1.FamilyId);
        v2.Version.ShouldBe(2);
        v2.Status.ShouldBe(RuleSetStatus.Draft);
        v2.Rules.ShouldHaveSingleItem();
        v2.Rules[0].Id.ShouldNotBe(v1.Rules[0].Id);
        v2.Rules[0].Condition.ShouldBe(v1.Rules[0].Condition);
        v2.Rules[0].Action.ShouldBe(v1.Rules[0].Action);

        Should.Throw<InvalidStateTransitionException>(() => v2.CreateNextVersion(Now));
        v1.Retire();
        v1.Status.ShouldBe(RuleSetStatus.Retired);
    }

    [Fact]
    public void Unknown_rule_id_is_a_domain_error()
    {
        var set = RuleSet.CreateDraft(TenantId.New(), "policy", "*", Now);
        Should.Throw<DomainException>(() => set.RemoveRule(Guid.NewGuid())).Code.ShouldBe("rule_not_found");
    }
}
