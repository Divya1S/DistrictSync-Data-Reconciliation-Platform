namespace DistrictSync.Domain.Rules;

public enum RuleActionKind
{
    /// <summary>Resolve the discrepancy immediately using a <see cref="ResolutionStrategy"/>; no human involved.</summary>
    AutoResolve = 1,

    /// <summary>Open a resolution case for a human to work, with a priority and optional role routing.</summary>
    OpenCase = 2,

    /// <summary>Record the discrepancy but take no action (e.g. terminated employees missing in payroll).</summary>
    Ignore = 3,
}

/// <summary>Which side wins when two systems disagree about a field.</summary>
public enum ResolutionStrategy
{
    PreferLeft = 1,
    PreferRight = 2,

    /// <summary>The record with the most recent source timestamp wins (last-writer-wins by source clock).</summary>
    PreferNewest = 3,

    /// <summary>The source system with the higher configured trust rank wins (system-of-record precedence).</summary>
    PreferHigherTrust = 4,
}

public enum CasePriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Urgent = 4,
}

/// <summary>What happens when a rule's condition matches. Owned value object on <see cref="Rule"/>.</summary>
public sealed record RuleAction(
    RuleActionKind Kind,
    ResolutionStrategy? Strategy = null,
    CasePriority? Priority = null,
    string? AssignToRole = null,
    string? Note = null)
{
    public static RuleAction AutoResolve(ResolutionStrategy strategy, string? note = null) => new(RuleActionKind.AutoResolve, strategy, Note: note);

    public static RuleAction OpenCase(CasePriority priority, string? assignToRole = null, string? note = null) => new(RuleActionKind.OpenCase, Priority: priority, AssignToRole: assignToRole, Note: note);

    public static RuleAction Ignore(string? note = null) => new(RuleActionKind.Ignore, Note: note);
}
