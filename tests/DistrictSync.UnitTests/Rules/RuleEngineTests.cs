using System.Text.Json.Nodes;
using DistrictSync.Application.Rules;
using DistrictSync.Application.Rules.Dsl;
using DistrictSync.Domain.Common;
using DistrictSync.Domain.Rules;
using Microsoft.Extensions.Logging.Abstractions;

namespace DistrictSync.UnitTests.Rules;

public class RuleEngineTests
{
    private static readonly RuleEngine Engine = new(new RuleExpressionCache(), NullLogger<RuleEngine>.Instance);

    private static JsonObject Facts(string type, params string[] fields) => new()
    {
        ["discrepancy"] = new JsonObject { ["type"] = type },
        ["fields"] = new JsonArray(fields.Select(f => (JsonNode)f).ToArray()),
    };

    private static RuleSet SampleRuleSet()
    {
        var set = RuleSet.CreateDraft(TenantId.New(), "policy", "Employee", DateTimeOffset.UtcNow);
        set.AddRule("ignore terminated", "discrepancy.type == \"MissingInRight\" and left.status == \"Terminated\"", RuleAction.Ignore());
        set.AddRule("salary → case", "fields.any(\"salary\")", RuleAction.OpenCase(CasePriority.High, "payroll"));
        set.AddRule("contact → auto", "fields.only(\"phone\", \"title\")", RuleAction.AutoResolve(ResolutionStrategy.PreferNewest));
        return set;
    }

    [Fact]
    public void First_matching_rule_wins_in_order()
    {
        var evaluation = Engine.Evaluate(CompiledRuleSet.From(SampleRuleSet()), Facts("FieldConflict", "salary", "phone"));
        evaluation.Matched.ShouldNotBeNull();
        evaluation.Matched.Name.ShouldBe("salary → case");
        evaluation.Action.Kind.ShouldBe(RuleActionKind.OpenCase);
        evaluation.Action.Priority.ShouldBe(CasePriority.High);
        evaluation.Trace.Count.ShouldBe(2);
        evaluation.Trace[0].Matched.ShouldBeFalse();
        evaluation.Trace[1].Matched.ShouldBeTrue();
    }

    [Fact]
    public void Later_rule_matches_when_earlier_do_not()
    {
        var evaluation = Engine.Evaluate(CompiledRuleSet.From(SampleRuleSet()), Facts("FieldConflict", "phone"));
        evaluation.Matched!.Name.ShouldBe("contact → auto");
        evaluation.Action.Strategy.ShouldBe(ResolutionStrategy.PreferNewest);
    }

    [Fact]
    public void Default_action_opens_a_normal_case_when_nothing_matches()
    {
        var evaluation = Engine.Evaluate(CompiledRuleSet.From(SampleRuleSet()), Facts("Duplicate"));
        evaluation.Matched.ShouldBeNull();
        evaluation.Action.Kind.ShouldBe(RuleActionKind.OpenCase);
        evaluation.Action.Priority.ShouldBe(CasePriority.Normal);
        evaluation.Trace.Count.ShouldBe(3);
    }

    [Fact]
    public void Null_rule_set_falls_back_to_default()
    {
        var evaluation = Engine.Evaluate(null, Facts("FieldConflict", "salary"));
        evaluation.Matched.ShouldBeNull();
        evaluation.Action.ShouldBe(RuleEvaluation.DefaultAction);
    }

    [Fact]
    public void Disabled_rules_are_excluded_from_the_compiled_set()
    {
        var set = SampleRuleSet();
        var salaryRule = set.Rules.Single(r => r.Name == "salary → case");
        set.UpdateRule(salaryRule.Id, salaryRule.Name, null, salaryRule.Condition, salaryRule.Action, isEnabled: false);

        var evaluation = Engine.Evaluate(CompiledRuleSet.From(set), Facts("FieldConflict", "salary"));
        evaluation.Matched.ShouldBeNull();
    }

    [Fact]
    public void A_rule_that_throws_is_skipped_and_traced_not_matched()
    {
        var set = RuleSet.CreateDraft(TenantId.New(), "policy", "*", DateTimeOffset.UtcNow);
        set.AddRule("broken regex", "discrepancy.type matches \"[\"", RuleAction.AutoResolve(ResolutionStrategy.PreferLeft));
        set.AddRule("fallback", "true", RuleAction.Ignore());

        var evaluation = Engine.Evaluate(CompiledRuleSet.From(set), Facts("FieldConflict"));
        evaluation.Matched!.Name.ShouldBe("fallback");
        evaluation.Trace[0].Error.ShouldNotBeNull();
        evaluation.Trace[0].Matched.ShouldBeFalse();
    }
}
