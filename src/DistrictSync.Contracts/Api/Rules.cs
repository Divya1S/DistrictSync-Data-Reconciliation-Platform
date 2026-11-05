namespace DistrictSync.Contracts.Api;

public sealed record RuleActionDto(string Kind, string? Strategy, string? Priority, string? AssignToRole, string? Note);

public sealed record RuleDto(Guid Id, string Name, string? Description, int Order, string Condition, RuleActionDto Action, bool IsEnabled);

public sealed record RuleSetDto(
    Guid Id,
    Guid FamilyId,
    string Name,
    string EntityType,
    int Version,
    string Status,
    IReadOnlyList<RuleDto> Rules,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    string? PublishedBy,
    string? ChangeNote);

public sealed record RuleSetSummaryDto(Guid Id, Guid FamilyId, string Name, string EntityType, int Version, string Status, int RuleCount, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt);

public sealed record CreateRuleSetRequest(string Name, string EntityType);

public sealed record UpsertRuleRequest(string Name, string? Description, string Condition, RuleActionDto Action, bool IsEnabled = true, int? Order = null);

public sealed record PublishRuleSetRequest(string? ChangeNote);

public sealed record ValidateRuleRequest(string Condition);

public sealed record ValidateRuleResponse(bool IsValid, string? Error, int? ErrorPosition, IReadOnlyList<string> ReferencedFacts);

public sealed record DryRunRequest(Guid DiscrepancyId);

public sealed record DryRunResponse(Guid DiscrepancyId, Guid? MatchedRuleId, string? MatchedRuleName, RuleActionDto? Action, IReadOnlyList<RuleEvaluationTraceDto> Trace);

public sealed record RuleEvaluationTraceDto(Guid RuleId, string RuleName, bool Matched, string? Error);
