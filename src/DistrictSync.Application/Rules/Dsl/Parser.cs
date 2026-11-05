using System.Globalization;

namespace DistrictSync.Application.Rules.Dsl;

/// <summary>
/// Recursive-descent parser for the rule language.
/// <code>
/// expression := or
/// or         := and   ( ("or"  | "||") and )*
/// and        := unary ( ("and" | "&amp;&amp;") unary )*
/// unary      := ("not" | "!") unary | comparison
/// comparison := primary ( op primary )?         op: == != &lt; &lt;= &gt; &gt;= in "not in" contains startsWith endsWith matches
/// primary    := number | string | true | false | null | "[" items "]" | "(" expression ")"
///             | path [ "(" args ")" ]           path: ident ("." ident)*
/// </code>
/// Keywords are matched case-insensitively. A path followed by "(" is a call: the last segment is the
/// function name and the preceding segments (if any) are the receiver.
/// </summary>
public sealed class Parser
{
    private static readonly HashSet<string> ComparisonWords = new(StringComparer.OrdinalIgnoreCase) { "in", "contains", "startsWith", "endsWith", "matches", "not" };

    private readonly List<Token> _tokens;
    private int _pos;

    private Parser(List<Token> tokens) => _tokens = tokens;

    public static Expr Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new RuleSyntaxException("Condition is empty.", 0);
        }

        var parser = new Parser(Lexer.Tokenize(source));
        var expr = parser.ParseOr();
        if (parser.Current.Kind != TokenKind.End)
        {
            throw new RuleSyntaxException($"Unexpected '{parser.Current.Text}'.", parser.Current.Position);
        }

        return expr;
    }

    private Token Current => _tokens[_pos];

    private Token Advance() => _tokens[_pos++];

    private bool IsWord(string word) => Current.Kind == TokenKind.Identifier && string.Equals(Current.Text, word, StringComparison.OrdinalIgnoreCase);

    private Expr ParseOr()
    {
        var left = ParseAnd();
        while (Current.Kind == TokenKind.OrOr || IsWord("or"))
        {
            var op = Advance();
            left = new BinaryExpr(BinaryOp.Or, left, ParseAnd(), op.Position);
        }

        return left;
    }

    private Expr ParseAnd()
    {
        var left = ParseUnary();
        while (Current.Kind == TokenKind.AndAnd || IsWord("and"))
        {
            var op = Advance();
            left = new BinaryExpr(BinaryOp.And, left, ParseUnary(), op.Position);
        }

        return left;
    }

    private Expr ParseUnary()
    {
        if (Current.Kind == TokenKind.Bang || (IsWord("not") && !IsNotInSequence()))
        {
            var op = Advance();
            return new NotExpr(ParseUnary(), op.Position);
        }

        return ParseComparison();
    }

    private bool IsNotInSequence() => _pos + 1 < _tokens.Count && _tokens[_pos + 1].Kind == TokenKind.Identifier && string.Equals(_tokens[_pos + 1].Text, "in", StringComparison.OrdinalIgnoreCase);

    private Expr ParseComparison()
    {
        var left = ParsePrimary();
        BinaryOp? op = Current.Kind switch
        {
            TokenKind.EqEq => BinaryOp.Eq,
            TokenKind.NotEq => BinaryOp.Ne,
            TokenKind.Lt => BinaryOp.Lt,
            TokenKind.Le => BinaryOp.Le,
            TokenKind.Gt => BinaryOp.Gt,
            TokenKind.Ge => BinaryOp.Ge,
            _ => null,
        };

        if (op is null && Current.Kind == TokenKind.Identifier && ComparisonWords.Contains(Current.Text))
        {
            if (IsWord("not"))
            {
                if (!IsNotInSequence())
                {
                    throw new RuleSyntaxException("Expected 'in' after 'not'.", Current.Position);
                }

                Advance();
                op = BinaryOp.NotIn;
            }
            else
            {
                op = Current.Text.ToLowerInvariant() switch
                {
                    "in" => BinaryOp.In,
                    "contains" => BinaryOp.Contains,
                    "startswith" => BinaryOp.StartsWith,
                    "endswith" => BinaryOp.EndsWith,
                    "matches" => BinaryOp.Matches,
                    _ => null,
                };
            }
        }

        if (op is null)
        {
            return left;
        }

        var opToken = Advance();
        var right = ParsePrimary();
        return new BinaryExpr(op.Value, left, right, opToken.Position);
    }

    private Expr ParsePrimary()
    {
        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.Number:
                Advance();
                return new LiteralExpr(RuleValue.From(decimal.Parse(token.Text, CultureInfo.InvariantCulture)), token.Position);
            case TokenKind.String:
                Advance();
                return new LiteralExpr(RuleValue.From(token.Text), token.Position);
            case TokenKind.LParen:
            {
                Advance();
                var inner = ParseOr();
                Expect(TokenKind.RParen, "')'");
                return inner;
            }

            case TokenKind.LBracket:
            {
                Advance();
                var items = new List<Expr>();
                while (Current.Kind != TokenKind.RBracket)
                {
                    items.Add(ParseOr());
                    if (Current.Kind == TokenKind.Comma)
                    {
                        Advance();
                        continue;
                    }

                    if (Current.Kind != TokenKind.RBracket)
                    {
                        throw new RuleSyntaxException("Expected ',' or ']' in list.", Current.Position);
                    }
                }

                Advance();
                return new ListExpr(items, token.Position);
            }

            case TokenKind.Identifier:
                return ParsePathOrCall();
            case TokenKind.End:
                throw new RuleSyntaxException("Unexpected end of condition.", token.Position);
            default:
                throw new RuleSyntaxException($"Unexpected '{token.Text}'.", token.Position);
        }
    }

    private Expr ParsePathOrCall()
    {
        var start = Current;
        var segments = new List<string>();
        switch (start.Text.ToLowerInvariant())
        {
            case "true":
                Advance();
                return new LiteralExpr(RuleValue.True, start.Position);
            case "false":
                Advance();
                return new LiteralExpr(RuleValue.False, start.Position);
            case "null":
                Advance();
                return new LiteralExpr(RuleValue.Null, start.Position);
        }

        segments.Add(Advance().Text);
        while (Current.Kind == TokenKind.Dot)
        {
            Advance();
            if (Current.Kind != TokenKind.Identifier)
            {
                throw new RuleSyntaxException("Expected a name after '.'.", Current.Position);
            }

            segments.Add(Advance().Text);
        }

        if (Current.Kind != TokenKind.LParen)
        {
            return new PathExpr(segments, start.Position);
        }

        Advance();
        var args = new List<Expr>();
        while (Current.Kind != TokenKind.RParen)
        {
            args.Add(ParseOr());
            if (Current.Kind == TokenKind.Comma)
            {
                Advance();
                continue;
            }

            if (Current.Kind != TokenKind.RParen)
            {
                throw new RuleSyntaxException("Expected ',' or ')' in argument list.", Current.Position);
            }
        }

        Advance();
        var name = segments[^1];
        Expr? receiver = segments.Count > 1 ? new PathExpr(segments.Take(segments.Count - 1).ToList(), start.Position) : null;
        return new CallExpr(name, receiver, args, start.Position);
    }

    private void Expect(TokenKind kind, string what)
    {
        if (Current.Kind != kind)
        {
            throw new RuleSyntaxException($"Expected {what}.", Current.Position);
        }

        Advance();
    }
}
