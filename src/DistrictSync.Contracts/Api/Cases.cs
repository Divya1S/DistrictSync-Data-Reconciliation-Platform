namespace DistrictSync.Contracts.Api;

public sealed record CaseDto(
    Guid Id,
    Guid DiscrepancyId,
    Guid RunId,
    string Title,
    string Priority,
    string State,
    string? AssignedRole,
    string? AssigneeId,
    int EscalationLevel,
    DateTimeOffset DueAt,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    bool IsOverdue,
    string ETag);

public sealed record CaseHistoryEntryDto(DateTimeOffset At, string Actor, string Action, string? FromState, string ToState, string? Note);

public sealed record ProposedResolutionDto(string? Strategy, string? ManualValuesJson, string Note, string ProposedBy, DateTimeOffset ProposedAt);

public sealed record CaseDetailDto(CaseDto Case, DiscrepancyDetailDto Discrepancy, ProposedResolutionDto? Proposal, string? ApprovedBy, IReadOnlyList<CaseHistoryEntryDto> History);

public sealed record AssignCaseRequest(string AssigneeId);

public sealed record ProposeResolutionRequest(string? Strategy, Dictionary<string, string?>? ManualValues, string Note);

public sealed record RejectProposalRequest(string Reason);

public sealed record CancelCaseRequest(string Reason);

public sealed record AddCommentRequest(string Note);
