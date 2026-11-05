using System.Text.Json.Nodes;
using DistrictSync.Application.Rules.Dsl;
using DistrictSync.Domain.Rules;
using Microsoft.Extensions.Logging;

namespace DistrictSync.Application.Rules;

public sealed record RuleTrace(Guid RuleId, string RuleName, bool Matched, string? Error);

public sealed record RuleEvaluation(CompiledRule? Matched, IReadOnlyList<RuleTrace> Trace, RuleAction Action)
{
    /// <summary>When nothing matches, a human looks at it. Failing safe is the whole point of an exception workflow.</summary>
    public static RuleAction DefaultAction { get; } = RuleAction.OpenCase(CasePriority.Normal, note: "No rule matched; routed to manual review.");
}

/// <summary>
/// First-match rule evaluation. Rules are ordered; the first whose condition is true decides the action.
/// A condition that throws (bad regex, unknown function) is logged and treated as not matched, so a
/// broken rule can never auto-resolve data by accident.
/// </summary>
public sealed class RuleEngine(RuleExpressionCache cache, ILogger<RuleEngine> logger)
{
    public RuleEvaluation Evaluate(CompiledRuleSet? ruleSet, JsonObject facts)
    {
        if (ruleSet is null || ruleSet.Rules.Count == 0)
        {
            return new RuleEvaluation(null, [], RuleEvaluation.DefaultAction);
        }

        var trace = new List<RuleTrace>(ruleSet.Rules.Count);
        foreach (var rule in ruleSet.Rules.OrderBy(r => r.Order))
        {
            try
            {
                var matched = cache.Get(rule.Condition).Evaluate(facts);
                trace.Add(new RuleTrace(rule.Id, rule.Name, matched, null));
                if (matched)
                {
                    return new RuleEvaluation(rule, trace, rule.Action);
                }
            }
            catch (Exception ex) when (ex is RuleSyntaxException or RuleEvaluationException)
            {
                logger.LogWarning(ex, "Rule {RuleName} ({RuleId}) in rule set {RuleSetId} v{Version} failed to evaluate and was skipped", rule.Name, rule.Id, ruleSet.Id, ruleSet.Version);
                trace.Add(new RuleTrace(rule.Id, rule.Name, false, ex.Message));
            }
        }

        return new RuleEvaluation(null, trace, RuleEvaluation.DefaultAction);
    }
}
