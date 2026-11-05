using System.Globalization;
using System.Text;

namespace DistrictSync.Application.Rules.Dsl;

public enum TokenKind
{
    Number,
    String,
    Identifier,
    LParen,
    RParen,
    LBracket,
    RBracket,
    Comma,
    Dot,
    EqEq,
    NotEq,
    Lt,
    Le,
    Gt,
    Ge,
    AndAnd,
    OrOr,
    Bang,
    End,
}

public readonly record struct Token(TokenKind Kind, string Text, int Position);

/// <summary>Hand-written tokenizer: fast, allocation-light, and gives precise error positions.</summary>
public static class Lexer
{
    public static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            var start = i;
            switch (c)
            {
                case '(':
                    tokens.Add(new Token(TokenKind.LParen, "(", start));
                    i++;
                    continue;
                case ')':
                    tokens.Add(new Token(TokenKind.RParen, ")", start));
                    i++;
                    continue;
                case '[':
                    tokens.Add(new Token(TokenKind.LBracket, "[", start));
                    i++;
                    continue;
                case ']':
                    tokens.Add(new Token(TokenKind.RBracket, "]", start));
                    i++;
                    continue;
                case ',':
                    tokens.Add(new Token(TokenKind.Comma, ",", start));
                    i++;
                    continue;
                case '.':
                    tokens.Add(new Token(TokenKind.Dot, ".", start));
                    i++;
                    continue;
                case '=':
                    i += Peek(source, i + 1) == '=' ? 2 : 1;
                    tokens.Add(new Token(TokenKind.EqEq, "==", start));
                    continue;
                case '!':
                    if (Peek(source, i + 1) == '=')
                    {
                        tokens.Add(new Token(TokenKind.NotEq, "!=", start));
                        i += 2;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenKind.Bang, "!", start));
                        i++;
                    }

                    continue;
                case '<':
                    if (Peek(source, i + 1) == '=')
                    {
                        tokens.Add(new Token(TokenKind.Le, "<=", start));
                        i += 2;
                    }
                    else if (Peek(source, i + 1) == '>')
                    {
                        tokens.Add(new Token(TokenKind.NotEq, "<>", start));
                        i += 2;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenKind.Lt, "<", start));
                        i++;
                    }

                    continue;
                case '>':
                    if (Peek(source, i + 1) == '=')
                    {
                        tokens.Add(new Token(TokenKind.Ge, ">=", start));
                        i += 2;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenKind.Gt, ">", start));
                        i++;
                    }

                    continue;
                case '&':
                    if (Peek(source, i + 1) != '&')
                    {
                        throw new RuleSyntaxException("Unexpected '&'; did you mean '&&' or 'and'?", start);
                    }

                    tokens.Add(new Token(TokenKind.AndAnd, "&&", start));
                    i += 2;
                    continue;
                case '|':
                    if (Peek(source, i + 1) != '|')
                    {
                        throw new RuleSyntaxException("Unexpected '|'; did you mean '||' or 'or'?", start);
                    }

                    tokens.Add(new Token(TokenKind.OrOr, "||", start));
                    i += 2;
                    continue;
                case '"':
                case '\'':
                    tokens.Add(ReadString(source, ref i));
                    continue;
            }

            if (char.IsAsciiDigit(c) || (c == '-' && char.IsAsciiDigit(Peek(source, i + 1))))
            {
                i++;
                while (i < source.Length && (char.IsAsciiDigit(source[i]) || source[i] == '.' || source[i] == '_'))
                {
                    i++;
                }

                var text = source[start..i].Replace("_", string.Empty, StringComparison.Ordinal);
                if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    throw new RuleSyntaxException($"Invalid number '{text}'.", start);
                }

                tokens.Add(new Token(TokenKind.Number, text, start));
                continue;
            }

            if (char.IsLetter(c) || c == '_' || c == '$')
            {
                i++;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_' || source[i] == '-' || source[i] == '$'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Identifier, source[start..i], start));
                continue;
            }

            throw new RuleSyntaxException($"Unexpected character '{c}'.", start);
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, source.Length));
        return tokens;
    }

    private static char Peek(string s, int index) => index < s.Length ? s[index] : '\0';

    private static Token ReadString(string source, ref int i)
    {
        var quote = source[i];
        var start = i;
        i++;
        var sb = new StringBuilder();
        while (i < source.Length)
        {
            var c = source[i];
            if (c == '\\' && i + 1 < source.Length)
            {
                var n = source[i + 1];
                sb.Append(n switch
                {
                    'n' => '\n',
                    't' => '\t',
                    _ => n,
                });
                i += 2;
                continue;
            }

            if (c == quote)
            {
                i++;
                return new Token(TokenKind.String, sb.ToString(), start);
            }

            sb.Append(c);
            i++;
        }

        throw new RuleSyntaxException("Unterminated string literal.", start);
    }
}
