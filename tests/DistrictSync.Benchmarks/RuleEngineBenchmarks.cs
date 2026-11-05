using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using DistrictSync.Application.Rules;
using DistrictSync.Application.Rules.Dsl;
using DistrictSync.Domain.Common;
using DistrictSync.Domain.Rules;
using Microsoft.Extensions.Logging.Abstractions;

namespace DistrictSync.Benchmarks;

[MemoryDiagnoser]
public class RuleEngineBenchmarks
{
    private readonly RuleEngine _engine = new(new RuleExpressionCache(), NullLogger<RuleEngine>.Instance);
    private CompiledRuleSet _ruleSet = null!;
    private JsonObject[] _facts = [];

    [GlobalSetup]
    public void Setup()
    {
        var set = RuleSet.CreateDraft(TenantId.New(), "policy", "Employee", DateTimeOffset.UtcNow);
        set.AddRule("terminated", "discrepancy.type == \"MissingInRight\" and left.status == \"Terminated\"", RuleAction.Ignore());
        set.AddRule("money", "discrepancy.type == \"FieldConflict\" and fields.any(\"salary\", \"status\")", RuleAction.OpenCase(CasePriority.High, "payroll"));
        set.AddRule("contact", "discrepancy.type == \"FieldConflict\" and fields.only(\"phone\", \"title\", \"department\")", RuleAction.AutoResolve(ResolutionStrategy.PreferNewest));
        set.AddRule("names", "discrepancy.type == \"FieldConflict\" and fields.only(\"firstName\", \"lastName\")", RuleAction.AutoResolve(ResolutionStrategy.PreferHigherTrust));
        set.AddRule("new hire", "discrepancy.type == \"MissingInRight\" and left.status == \"Active\" and daysSince(left.hireDate) <= 30", RuleAction.OpenCase(CasePriority.Urgent, "payroll"));
        set.AddRule("dupes", "discrepancy.type == \"Duplicate\"", RuleAction.OpenCase(CasePriority.Normal));
        _ruleSet = CompiledRuleSet.From(set);

        var fieldSets = new[] { new[] { "phone" }, new[] { "salary" }, new[] { "firstName", "lastName" }, new[] { "department", "title" } };
        _facts = Enumerable.Range(0, 1000).Select(i => new JsonObject
        {
            ["_now"] = "2026-11-04T00:00:00Z",
            ["discrepancy"] = new JsonObject { ["type"] = i % 10 == 0 ? "MissingInRight" : "FieldConflict", ["severity"] = "Low" },
            ["fields"] = new JsonArray(fieldSets[i % fieldSets.Length].Select(f => (JsonNode)f).ToArray()),
            ["left"] = new JsonObject { ["_exists"] = true, ["status"] = i % 3 == 0 ? "Terminated" : "Active", ["hireDate"] = "2026-10-20" },
            ["right"] = new JsonObject { ["_exists"] = i % 10 != 0 },
        }).ToArray();
    }

    [Benchmark]
    public int Evaluate1000Discrepancies()
    {
        var matched = 0;
        foreach (var facts in _facts)
        {
            if (_engine.Evaluate(_ruleSet, facts).Matched is not null)
            {
                matched++;
            }
        }

        return matched;
    }

    [Benchmark]
    public RuleExpression ParseCondition() => RuleExpression.Compile("discrepancy.type == \"FieldConflict\" and fields.only(\"phone\", \"title\", \"department\") and daysSince(left.hireDate) <= 30");
}
