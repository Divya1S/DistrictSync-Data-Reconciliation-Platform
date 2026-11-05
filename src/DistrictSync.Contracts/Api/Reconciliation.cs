using DistrictSync.Contracts.Events;

namespace DistrictSync.Contracts.Api;

public sealed record CreateReconciliationRunRequest(string EntityType, Guid LeftSourceSystemId, Guid RightSourceSystemId, Guid? ComparisonProfileId);

public sealed record ReconciliationRunDto(
    Guid Id,
    string EntityType,
    Guid LeftSourceSystemId,
    string LeftSourceSystemCode,
    Guid RightSourceSystemId,
    string RightSourceSystemCode,
    string Status,
    ReconciliationStatsDto Stats,
    Guid? RuleSetId,
    int? RuleSetVersion,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason,
    string RequestedBy,
    string? Checkpoint);

public sealed record FieldDifferenceDto(string Field, string? LeftValue, string? RightValue, bool IsCritical);

public sealed record DiscrepancyDto(
    Guid Id,
    Guid RunId,
    string EntityType,
    string Type,
    string Status,
    string Severity,
    string MatchKey,
    Guid LeftSourceSystemId,
    Guid RightSourceSystemId,
    Guid? LeftRecordId,
    Guid? RightRecordId,
    IReadOnlyList<FieldDifferenceDto> Differences,
    Guid? AppliedRuleId,
    string? ResolutionNote,
    DateTimeOffset DetectedAt,
    DateTimeOffset? ResolvedAt);

public sealed record SourceRecordDto(Guid Id, Guid SourceSystemId, string ExternalId, string MatchKey, Dictionary<string, string?> Attributes, DateTimeOffset? SourceUpdatedAt, DateTimeOffset IngestedAt, bool IsDeleted);

public sealed record DiscrepancyDetailDto(DiscrepancyDto Discrepancy, SourceRecordDto? Left, SourceRecordDto? Right, Guid? CaseId);

public sealed record ComparedFieldDto(string Name, string Normalizer, bool IsCritical);

public sealed record ComparisonProfileDto(Guid Id, string Name, string EntityType, IReadOnlyList<ComparedFieldDto> Fields, bool IsDefault, DateTimeOffset CreatedAt);

public sealed record CreateComparisonProfileRequest(string Name, string EntityType, IReadOnlyList<ComparedFieldDto> Fields, bool IsDefault);
