using DistrictSync.Domain.Common;

namespace DistrictSync.Domain.Reconciliation;

public enum DiscrepancyType
{
    /// <summary>Key exists in the right source but not in the left.</summary>
    MissingInLeft = 1,

    /// <summary>Key exists in the left source but not in the right.</summary>
    MissingInRight = 2,

    /// <summary>Both sides have the key but one or more compared fields differ.</summary>
    FieldConflict = 3,

    /// <summary>The same match key appears more than once within a single source.</summary>
    Duplicate = 4,
}

public enum DiscrepancyStatus
{
    Open = 1,
    AutoResolved = 2,
    CaseOpened = 3,
    Resolved = 4,
    Ignored = 5,
}

public enum Severity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

public sealed record FieldDifference(string Field, string? LeftValue, string? RightValue, bool IsCritical);

/// <summary>
/// One actionable finding from a reconciliation run. Status transitions are driven by the rule engine
/// (auto-resolution) or by a <c>ResolutionCase</c> workflow (human decision).
/// </summary>
public sealed class Discrepancy : AggregateRoot, ITenantScoped
{
    private readonly List<FieldDifference> _differences = [];

    private Discrepancy() { }

    public TenantId TenantId { get; private set; }
    public Guid RunId { get; private set; }
    public string EntityType { get; private set; } = string.Empty;
    public DiscrepancyType Type { get; private set; }
    public DiscrepancyStatus Status { get; private set; }
    public Severity Severity { get; private set; }
    public string MatchKey { get; private set; } = string.Empty;
    public Guid LeftSourceSystemId { get; private set; }
    public Guid RightSourceSystemId { get; private set; }
    public Guid? LeftRecordId { get; private set; }
    public Guid? RightRecordId { get; private set; }
    public IReadOnlyCollection<FieldDifference> Differences => _differences.AsReadOnly();
    public Guid? AppliedRuleId { get; private set; }
    public string? ResolutionNote { get; private set; }
    public DateTimeOffset DetectedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public static Discrepancy Detect(
        TenantId tenantId,
        Guid runId,
        string entityType,
        DiscrepancyType type,
        string matchKey,
        Guid leftSourceSystemId,
        Guid rightSourceSystemId,
        Guid? leftRecordId,
        Guid? rightRecordId,
        IEnumerable<FieldDifference> differences,
        DateTimeOffset now)
    {
        var d = new Discrepancy
        {
            TenantId = tenantId,
            RunId = runId,
            EntityType = entityType,
            Type = type,
            Status = DiscrepancyStatus.Open,
            MatchKey = Guard.MaxLength(matchKey, 256),
            LeftSourceSystemId = leftSourceSystemId,
            RightSourceSystemId = rightSourceSystemId,
            LeftRecordId = leftRecordId,
            RightRecordId = rightRecordId,
            DetectedAt = now,
        };
        d._differences.AddRange(differences);
        d.Severity = d.ComputeSeverity();
        return d;
    }

    private Severity ComputeSeverity() => Type switch
    {
        DiscrepancyType.Duplicate => Severity.Medium,
        DiscrepancyType.FieldConflict when _differences.Any(x => x.IsCritical) => Severity.High,
        DiscrepancyType.FieldConflict => Severity.Low,
        _ => Severity.Medium,
    };

    public void AutoResolve(Guid ruleId, string note, DateTimeOffset now)
    {
        EnsureOpen(nameof(AutoResolve));
        Status = DiscrepancyStatus.AutoResolved;
        AppliedRuleId = ruleId;
        ResolutionNote = Guard.MaxLength(note, 1000);
        ResolvedAt = now;
    }

    public void Ignore(Guid? ruleId, string note, DateTimeOffset now)
    {
        EnsureOpen(nameof(Ignore));
        Status = DiscrepancyStatus.Ignored;
        AppliedRuleId = ruleId;
        ResolutionNote = Guard.MaxLength(note, 1000);
        ResolvedAt = now;
    }

    public void EscalateToCase(Guid? ruleId)
    {
        EnsureOpen(nameof(EscalateToCase));
        Status = DiscrepancyStatus.CaseOpened;
        AppliedRuleId = ruleId;
    }

    public void Resolve(string note, DateTimeOffset now)
    {
        if (Status is not (DiscrepancyStatus.CaseOpened or DiscrepancyStatus.Open))
        {
            throw new InvalidStateTransitionException(nameof(Discrepancy), Status.ToString(), nameof(Resolve));
        }

        Status = DiscrepancyStatus.Resolved;
        ResolutionNote = Guard.MaxLength(note, 1000);
        ResolvedAt = now;
    }

    public void OverrideSeverity(Severity severity) => Severity = severity;

    private void EnsureOpen(string action)
    {
        if (Status != DiscrepancyStatus.Open)
        {
            throw new InvalidStateTransitionException(nameof(Discrepancy), Status.ToString(), action);
        }
    }
}
