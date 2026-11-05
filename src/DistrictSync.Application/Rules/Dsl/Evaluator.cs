using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DistrictSync.Application.Rules.Dsl;

/// <summary>Tree-walking evaluator. Missing facts evaluate to null rather than failing, so rules stay robust to sparse records.</summary>
public static class Evaluator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static RuleValue Evaluate(Expr expr, JsonObject facts) => expr switch
    {
        LiteralExpr lit => lit.Value,
        PathExpr path => ResolvePath(facts, path.Segments),
        ListExpr list => RuleValue.From(list.Items.Select(i => Evaluate(i, facts)).ToList()),
        NotExpr not => RuleValue.From(!Evaluate(not.Operand, facts).IsTruthy),
        BinaryExpr bin => EvaluateBinary(bin, facts),
        CallExpr call => EvaluateCall(call, facts),
        _ => throw new RuleEvaluationException($"Unknown expression node {expr.GetType().Name}."),
    };

    public static RuleValue ResolvePath(JsonObject facts, IReadOnlyList<string> segments)
    {
        JsonNode? node = facts;
        foreach (var segment in segments)
        {
            if (node is not JsonObject obj)
            {
                return RuleValue.Null;
            }

            // Fact names are matched case-insensitively so "Department" and "department" both work.
            var match = obj.FirstOrDefault(kv => string.Equals(kv.Key, segment, StringComparison.OrdinalIgnoreCase));
            if (match.Key is null)
            {
                return RuleValue.Null;
            }

            node = match.Value;
        }

        return RuleValue.FromJson(node);
    }

    private static RuleValue EvaluateBinary(BinaryExpr bin, JsonObject facts)
    {
        switch (bin.Op)
        {
            case BinaryOp.And:
                return RuleValue.From(Evaluate(bin.Left, facts).IsTruthy && Evaluate(bin.Right, facts).IsTruthy);
            case BinaryOp.Or:
                return RuleValue.From(Evaluate(bin.Left, facts).IsTruthy || Evaluate(bin.Right, facts).IsTruthy);
        }

        var left = Evaluate(bin.Left, facts);
        var right = Evaluate(bin.Right, facts);
        return bin.Op switch
        {
            BinaryOp.Eq => RuleValue.From(RuleValue.LooseEquals(left, right)),
            BinaryOp.Ne => RuleValue.From(!RuleValue.LooseEquals(left, right)),
            BinaryOp.Lt => RuleValue.From(RuleValue.Compare(left, right) is < 0),
            BinaryOp.Le => RuleValue.From(RuleValue.Compare(left, right) is <= 0),
            BinaryOp.Gt => RuleValue.From(RuleValue.Compare(left, right) is > 0),
            BinaryOp.Ge => RuleValue.From(RuleValue.Compare(left, right) is >= 0),
            BinaryOp.In => RuleValue.From(right.AsList().Any(item => RuleValue.LooseEquals(left, item))),
            BinaryOp.NotIn => RuleValue.From(!right.AsList().Any(item => RuleValue.LooseEquals(left, item))),
            BinaryOp.Contains => RuleValue.From(Contains(left, right)),
            BinaryOp.StartsWith => RuleValue.From(!left.IsNull && left.AsString().StartsWith(right.AsString(), StringComparison.OrdinalIgnoreCase)),
            BinaryOp.EndsWith => RuleValue.From(!left.IsNull && left.AsString().EndsWith(right.AsString(), StringComparison.OrdinalIgnoreCase)),
            BinaryOp.Matches => RuleValue.From(Matches(left, right)),
            _ => throw new RuleEvaluationException($"Unsupported operator {bin.Op}."),
        };
    }

    private static bool Contains(RuleValue haystack, RuleValue needle)
    {
        if (haystack.Kind == RuleValueKind.List)
        {
            return haystack.AsList().Any(item => RuleValue.LooseEquals(item, needle));
        }

        return !haystack.IsNull && haystack.AsString().Contains(needle.AsString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool Matches(RuleValue input, RuleValue pattern)
    {
        if (input.IsNull)
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(input.AsString(), pattern.AsString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new RuleEvaluationException($"Invalid regular expression '{pattern.AsString()}': {ex.Message}");
        }
        catch (RegexMatchTimeoutException)
        {
            throw new RuleEvaluationException($"Regular expression '{pattern.AsString()}' timed out.");
        }
    }

    private static RuleValue EvaluateCall(CallExpr call, JsonObject facts)
    {
        var args = call.Args.Select(a => Evaluate(a, facts)).ToList();
        var name = call.Name.ToLowerInvariant();

        if (call.Receiver is not null)
        {
            var receiver = Evaluate(call.Receiver, facts);
            return name switch
            {
                "any" => RuleValue.From(args.Count == 0 ? receiver.AsList().Count > 0 : receiver.AsList().Any(item => args.Any(a => RuleValue.LooseEquals(item, a)))),
                "all" => RuleValue.From(args.All(a => receiver.AsList().Any(item => RuleValue.LooseEquals(item, a)))),
                "only" => RuleValue.From(receiver.AsList().Count > 0 && receiver.AsList().All(item => args.Any(a => RuleValue.LooseEquals(item, a)))),
                "none" => RuleValue.From(!receiver.AsList().Any(item => args.Any(a => RuleValue.LooseEquals(item, a)))),
                "contains" => RuleValue.From(args.Count == 1 && Contains(receiver, args[0])),
                "count" or "length" or "len" => RuleValue.From(receiver.Kind == RuleValueKind.List ? receiver.AsList().Count : receiver.AsString().Length),
                "exists" => RuleValue.From(!receiver.IsNull),
                "lower" => RuleValue.From(receiver.AsString().ToLowerInvariant()),
                "upper" => RuleValue.From(receiver.AsString().ToUpperInvariant()),
                "trim" => RuleValue.From(receiver.AsString().Trim()),
                "startswith" => RuleValue.From(args.Count == 1 && receiver.AsString().StartsWith(args[0].AsString(), StringComparison.OrdinalIgnoreCase)),
                "endswith" => RuleValue.From(args.Count == 1 && receiver.AsString().EndsWith(args[0].AsString(), StringComparison.OrdinalIgnoreCase)),
                "matches" => RuleValue.From(args.Count == 1 && Matches(receiver, args[0])),
                _ => throw new RuleEvaluationException($"Unknown method '{call.Name}'."),
            };
        }

        return name switch
        {
            "len" or "length" or "count" => RuleValue.From(Arg(args, 0, name).Kind == RuleValueKind.List ? args[0].AsList().Count : args[0].AsString().Length),
            "lower" => RuleValue.From(Arg(args, 0, name).AsString().ToLowerInvariant()),
            "upper" => RuleValue.From(Arg(args, 0, name).AsString().ToUpperInvariant()),
            "trim" => RuleValue.From(Arg(args, 0, name).AsString().Trim()),
            "abs" => RuleValue.From(Arg(args, 0, name).TryAsNumber(out var n) ? Math.Abs(n) : 0),
            "number" => Arg(args, 0, name).TryAsNumber(out var num) ? RuleValue.From(num) : RuleValue.Null,
            "coalesce" => args.FirstOrDefault(a => !a.IsNull),
            "isnull" or "isempty" => RuleValue.From(!Arg(args, 0, name).IsTruthy),
            "dayssince" => Arg(args, 0, name).TryAsDateTime(out var d) ? RuleValue.From((decimal)(Now(facts) - d).TotalDays) : RuleValue.Null,
            "hourssince" => Arg(args, 0, name).TryAsDateTime(out var h) ? RuleValue.From((decimal)(Now(facts) - h).TotalHours) : RuleValue.Null,
            "diff" => Diff(args, name),
            _ => throw new RuleEvaluationException($"Unknown function '{call.Name}'."),
        };
    }

    /// <summary>Absolute numeric difference between two values: <c>diff(left.salary, right.salary) &gt; 1000</c>.</summary>
    private static RuleValue Diff(List<RuleValue> args, string name)
    {
        if (args.Count != 2)
        {
            throw new RuleEvaluationException($"'{name}' expects 2 arguments.");
        }

        return args[0].TryAsNumber(out var a) && args[1].TryAsNumber(out var b) ? RuleValue.From(Math.Abs(a - b)) : RuleValue.Null;
    }

    private static RuleValue Arg(List<RuleValue> args, int index, string function)
    {
        if (args.Count <= index)
        {
            throw new RuleEvaluationException($"'{function}' expects at least {index + 1} argument(s).");
        }

        return args[index];
    }

    /// <summary>Rules see a frozen "now" injected by the engine (fact <c>_now</c>) so evaluation is deterministic and testable.</summary>
    private static DateTimeOffset Now(JsonObject facts) => RuleValue.FromJson(facts["_now"]).TryAsDateTime(out var now) ? now : DateTimeOffset.UtcNow;
}
