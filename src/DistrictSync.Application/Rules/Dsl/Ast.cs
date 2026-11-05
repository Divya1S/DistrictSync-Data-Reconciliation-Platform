namespace DistrictSync.Application.Rules.Dsl;

public enum BinaryOp
{
    And,
    Or,
    Eq,
    Ne,
    Lt,
    Le,
    Gt,
    Ge,
    In,
    NotIn,
    Contains,
    StartsWith,
    EndsWith,
    Matches,
}

public abstract record Expr(int Position);

public sealed record LiteralExpr(RuleValue Value, int Position) : Expr(Position);

/// <summary>A dotted fact reference such as <c>discrepancy.type</c> or <c>left.department</c>.</summary>
public sealed record PathExpr(IReadOnlyList<string> Segments, int Position) : Expr(Position)
{
    public string Dotted => string.Join('.', Segments);
}

public sealed record ListExpr(IReadOnlyList<Expr> Items, int Position) : Expr(Position);

public sealed record NotExpr(Expr Operand, int Position) : Expr(Position);

public sealed record BinaryExpr(BinaryOp Op, Expr Left, Expr Right, int Position) : Expr(Position);

/// <summary>A function call. With a receiver it is method-style (<c>fields.any("salary")</c>); without, global (<c>len(fields)</c>).</summary>
public sealed record CallExpr(string Name, Expr? Receiver, IReadOnlyList<Expr> Args, int Position) : Expr(Position);
