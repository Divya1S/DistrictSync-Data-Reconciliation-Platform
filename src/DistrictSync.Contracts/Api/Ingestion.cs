namespace DistrictSync.Contracts.Api;

public sealed record IngestionBatchDto(
    Guid Id,
    Guid SourceSystemId,
    string SourceSystemCode,
    string EntityType,
    string Status,
    string? FileName,
    long SizeBytes,
    int TotalRecords,
    int AcceptedRecords,
    int RejectedRecords,
    int DuplicateRecords,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason,
    string SubmittedBy);

/// <summary>One record pushed through the webhook endpoint.</summary>
public sealed record WebhookRecord(string ExternalId, string? MatchKey, DateTimeOffset? UpdatedAt, bool Deleted, Dictionary<string, string?> Attributes);

public sealed record WebhookIngestRequest(string EntityType, IReadOnlyList<WebhookRecord> Records);

public sealed record WebhookIngestResponse(Guid BatchId, int Accepted, int Updated, int Unchanged, int Rejected, IReadOnlyList<string> Errors);

public sealed record StartPullRequest(Guid SourceSystemId, string EntityType);
