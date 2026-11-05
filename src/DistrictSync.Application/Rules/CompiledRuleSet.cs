using DistrictSync.Domain.Rules;

namespace DistrictSync.Application.Rules;

/// <summary>
/// Cache-friendly snapshot of a published rule set (plain records, no EF graph). The engine evaluates
/// this, never the entity, so hot paths don't touch the database and the cache can live in Redis.
/// </summary>
public sealed record CompiledRuleSet(Guid Id, Guid FamilyId, string Name, string EntityType, int Version, IReadOnlyList<CompiledRule> Rules)
{
    public static CompiledRuleSet From(RuleSet ruleSet) => new(
        ruleSet.Id,
        ruleSet.FamilyId,
        ruleSet.Name,
        ruleSet.EntityType,
        ruleSet.Version,
        ruleSet.Rules.Where(r => r.IsEnabled).Select(r => new CompiledRule(r.Id, r.Name, r.Order, r.Condition, r.Action)).ToList());
}

public sealed record CompiledRule(Guid Id, string Name, int Order, string Condition, RuleAction Action);
