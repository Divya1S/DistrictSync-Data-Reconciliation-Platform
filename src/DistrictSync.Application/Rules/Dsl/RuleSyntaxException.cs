namespace DistrictSync.Application.Rules.Dsl;

public sealed class RuleSyntaxException(string message, int position) : Exception($"{message} (at position {position})")
{
    public int Position { get; } = position;
    public string Detail { get; } = message;
}

public sealed class RuleEvaluationException(string message) : Exception(message);
