using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace DistrictSync.Application.Rules.Dsl;

/// <summary>A parsed, reusable rule condition.</summary>
public sealed class RuleExpression
{
    private RuleExpression(string source, Expr root, IReadOnlySet<string> referencedFacts)
    {
        Source = source;
        Root = root;
        ReferencedFacts = referencedFacts;
    }

    public string Source { get; }
    public Expr Root { get; }

    /// <summary>Dotted fact paths the condition reads; surfaced by the validation endpoint to help rule authors.</summary>
    public IReadOnlySet<string> ReferencedFacts { get; }

    public static RuleExpression Compile(string source)
    {
        var root = Parser.Parse(source);
        var facts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(root, facts);
        return new RuleExpression(source, root, facts);
    }

    public static bool TryCompile(string source, out RuleExpression? expression, out RuleSyntaxException? error)
    {
        try
        {
            expression = Compile(source);
            error = null;
            return true;
        }
        catch (RuleSyntaxException ex)
        {
            expression = null;
            error = ex;
            return false;
        }
    }

    public bool Evaluate(JsonObject facts) => Evaluator.Evaluate(Root, facts).IsTruthy;

    private static void Collect(Expr expr, ISet<string> facts)
    {
        switch (expr)
        {
            case PathExpr p:
                facts.Add(p.Dotted);
                break;
            case ListExpr l:
                foreach (var item in l.Items)
                {
                    Collect(item, facts);
                }

                break;
            case NotExpr n:
                Collect(n.Operand, facts);
                break;
            case BinaryExpr b:
                Collect(b.Left, facts);
                Collect(b.Right, facts);
                break;
            case CallExpr c:
                if (c.Receiver is not null)
                {
                    Collect(c.Receiver, facts);
                }

                foreach (var arg in c.Args)
                {
                    Collect(arg, facts);
                }

                break;
        }
    }
}

/// <summary>Process-wide cache of compiled conditions keyed by source text. Rule text is small and repetitive, so a bounded dictionary is enough.</summary>
public sealed class RuleExpressionCache
{
    private const int MaxEntries = 5_000;
    private readonly ConcurrentDictionary<string, RuleExpression> _cache = new(StringComparer.Ordinal);

    public RuleExpression Get(string source)
    {
        if (_cache.TryGetValue(source, out var cached))
        {
            return cached;
        }

        var compiled = RuleExpression.Compile(source);
        if (_cache.Count >= MaxEntries)
        {
            _cache.Clear();
        }

        _cache[source] = compiled;
        return compiled;
    }
}
