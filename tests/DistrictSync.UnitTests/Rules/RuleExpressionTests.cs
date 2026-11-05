using System.Text.Json.Nodes;
using DistrictSync.Application.Rules.Dsl;

namespace DistrictSync.UnitTests.Rules;

public class RuleExpressionTests
{
    private static readonly JsonObject Facts = new()
    {
        ["_now"] = "2026-11-04T12:00:00Z",
        ["discrepancy"] = new JsonObject { ["type"] = "FieldConflict", ["severity"] = "High", ["differenceCount"] = 2 },
        ["fields"] = new JsonArray("salary", "title"),
        ["left"] = new JsonObject { ["_exists"] = true, ["status"] = "Active", ["salary"] = "61,500.00", ["hireDate"] = "2026-10-20", ["email"] = "Ann.Lee@Northgate.edu", ["department"] = null },
        ["right"] = new JsonObject { ["_exists"] = true, ["status"] = "active", ["salary"] = "60000" },
        ["source"] = new JsonObject { ["left"] = new JsonObject { ["code"] = "HR", ["trustRank"] = 90 }, ["right"] = new JsonObject { ["code"] = "PAYROLL", ["trustRank"] = 70 } },
    };

    [Theory]
    [InlineData("discrepancy.type == \"FieldConflict\"", true)]
    [InlineData("discrepancy.type = 'fieldconflict'", true)]
    [InlineData("discrepancy.type != \"FieldConflict\"", false)]
    [InlineData("discrepancy.type <> \"Duplicate\"", true)]
    [InlineData("discrepancy.differenceCount >= 2", true)]
    [InlineData("discrepancy.differenceCount > 2", false)]
    [InlineData("left.status == right.status", true)]
    [InlineData("left.salary > right.salary", true)]
    [InlineData("diff(left.salary, right.salary) > 1000", true)]
    [InlineData("diff(left.salary, right.salary) > 2000", false)]
    [InlineData("source.left.trustRank > source.right.trustRank", true)]
    [InlineData("discrepancy.type in [\"FieldConflict\", \"Duplicate\"]", true)]
    [InlineData("discrepancy.type not in [\"MissingInLeft\", \"MissingInRight\"]", true)]
    [InlineData("fields.any(\"salary\")", true)]
    [InlineData("fields.any(\"phone\")", false)]
    [InlineData("fields.all(\"salary\", \"title\")", true)]
    [InlineData("fields.all(\"salary\", \"phone\")", false)]
    [InlineData("fields.only(\"salary\", \"title\", \"phone\")", true)]
    [InlineData("fields.only(\"salary\")", false)]
    [InlineData("fields.none(\"phone\", \"address\")", true)]
    [InlineData("fields contains \"title\"", true)]
    [InlineData("fields.count() == 2", true)]
    [InlineData("len(fields) == 2", true)]
    [InlineData("left.email endsWith \"@northgate.edu\"", true)]
    [InlineData("left.email startsWith \"ann.\"", true)]
    [InlineData("left.email matches \"^[a-z.]+@northgate\\.edu$\"", true)]
    [InlineData("lower(left.email) == \"ann.lee@northgate.edu\"", true)]
    [InlineData("left.department == null", true)]
    [InlineData("left.department.exists()", false)]
    [InlineData("left.missingField == null", true)]
    [InlineData("left.missingField > 5", false)]
    [InlineData("right._exists and left._exists", true)]
    [InlineData("not right._exists", false)]
    [InlineData("!right._exists || left._exists", true)]
    [InlineData("daysSince(left.hireDate) <= 30", true)]
    [InlineData("daysSince(left.hireDate) > 30", false)]
    [InlineData("coalesce(left.department, \"unknown\") == \"unknown\"", true)]
    [InlineData("DISCREPANCY.Type == \"FieldConflict\" AND Fields.Any(\"SALARY\")", true)]
    [InlineData("true", true)]
    [InlineData("false or (1 < 2 and 2 < 3)", true)]
    public void Evaluates_conditions(string condition, bool expected)
    {
        var expression = RuleExpression.Compile(condition);
        expression.Evaluate(Facts).ShouldBe(expected, condition);
    }

    [Fact]
    public void And_binds_tighter_than_or()
    {
        // false or (true and true) → true; (false or true) and false → would be false
        RuleExpression.Compile("discrepancy.type == \"X\" or discrepancy.severity == \"High\" and fields.any(\"salary\")").Evaluate(Facts).ShouldBeTrue();
        RuleExpression.Compile("discrepancy.type == \"X\" or discrepancy.severity == \"High\" and fields.any(\"nope\")").Evaluate(Facts).ShouldBeFalse();
    }

    [Fact]
    public void Collects_referenced_facts()
    {
        var expression = RuleExpression.Compile("discrepancy.type == \"FieldConflict\" and fields.only(\"phone\") and diff(left.salary, right.salary) > 10");
        expression.ReferencedFacts.ShouldBe(["discrepancy.type", "fields", "left.salary", "right.salary"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("discrepancy.type == \"unterminated", 20)]
    [InlineData("fields.any(\"a\"", 14)]
    [InlineData("left.salary >", 13)]
    [InlineData("(1 == 1", 7)]
    [InlineData("1 == 1 2", 7)]
    [InlineData("a & b", 2)]
    [InlineData("", 0)]
    public void Reports_syntax_errors_with_position(string condition, int expectedPosition)
    {
        var ok = RuleExpression.TryCompile(condition, out var expression, out var error);
        ok.ShouldBeFalse();
        expression.ShouldBeNull();
        error.ShouldNotBeNull();
        error.Position.ShouldBe(expectedPosition);
    }

    [Fact]
    public void Unknown_function_fails_at_evaluation_not_parse()
    {
        var expression = RuleExpression.Compile("frobnicate(left.salary) > 1");
        Should.Throw<RuleEvaluationException>(() => expression.Evaluate(Facts));
    }

    [Fact]
    public void Invalid_regex_is_an_evaluation_error()
    {
        var expression = RuleExpression.Compile("left.email matches \"[\"");
        Should.Throw<RuleEvaluationException>(() => expression.Evaluate(Facts));
    }

    [Fact]
    public void Cache_returns_same_instance_for_same_source()
    {
        var cache = new RuleExpressionCache();
        var a = cache.Get("fields.any(\"x\")");
        var b = cache.Get("fields.any(\"x\")");
        ReferenceEquals(a, b).ShouldBeTrue();
    }

    [Fact]
    public void Loose_equality_treats_numeric_strings_as_numbers_and_ignores_case()
    {
        RuleValue.LooseEquals(RuleValue.From("60,000"), RuleValue.From(60000m)).ShouldBeTrue();
        RuleValue.LooseEquals(RuleValue.From("Active"), RuleValue.From("ACTIVE ")).ShouldBeTrue();
        RuleValue.LooseEquals(RuleValue.Null, RuleValue.From("")).ShouldBeFalse();
        RuleValue.Compare(RuleValue.From("2026-01-02"), RuleValue.From("2026-01-01")).ShouldBe(1);
        RuleValue.Compare(RuleValue.Null, RuleValue.From(1m)).ShouldBeNull();
    }
}
