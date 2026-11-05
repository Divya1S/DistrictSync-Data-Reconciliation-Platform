using DistrictSync.Domain.Common;

namespace DistrictSync.Application.Abstractions;

/// <summary>Durable staging for raw uploads between API intake and worker processing.</summary>
public interface IStagingStore
{
    Task<string> StoreAsync(TenantId tenantId, Guid batchId, Stream content, CancellationToken ct);

    Task<Stream> OpenReadAsync(string reference, CancellationToken ct);

    Task DeleteAsync(string reference, CancellationToken ct);
}

/// <summary>A raw record as it arrives from a source (before normalisation).</summary>
public sealed record RawRecord(string? ExternalId, IReadOnlyDictionary<string, string?> Attributes, DateTimeOffset? UpdatedAt, bool Deleted, long LineNumber);

/// <summary>Streams raw records for a batch from its staged file or from the remote system, without buffering the whole set.</summary>
public interface IRawRecordSource
{
    IAsyncEnumerable<RawRecord> ReadAsync(Domain.Ingestion.IngestionBatch batch, Domain.Sources.SourceSystem source, CancellationToken ct);
}

/// <summary>A record projected for merge-join: ordered by MatchKey then Id.</summary>
public sealed record KeyedRecord(Guid Id, string MatchKey, string ExternalId, string PayloadJson, DateTimeOffset? SourceUpdatedAt);

/// <summary>
/// Streams one source's records for an entity type in match-key order using keyset pagination. Memory
/// is bounded by the page size regardless of table size, which is what lets a run compare millions of rows.
/// </summary>
public interface ISourceRecordStreamReader
{
    IAsyncEnumerable<KeyedRecord> StreamAsync(TenantId tenantId, Guid sourceSystemId, string entityType, string? afterMatchKey, int pageSize, CancellationToken ct);
}

public enum WriteBackAction
{
    NoOp = 0,
    UpdateFields = 1,
    CreateRecord = 2,
    DeleteRecord = 3,
}

public sealed record WriteBackRequest(TenantId TenantId, Guid TargetSourceSystemId, string EntityType, string MatchKey, WriteBackAction Action, string PayloadJson, string IdempotencyKey, string Explanation);

public sealed record WriteBackResult(bool Succeeded, string Message, bool IsTransient);

/// <summary>Pushes an agreed resolution back to the system that was wrong. Implementations must be idempotent on <see cref="WriteBackRequest.IdempotencyKey"/>.</summary>
public interface IWriteBackConnector
{
    Task<WriteBackResult> ApplyAsync(WriteBackRequest request, CancellationToken ct);
}

/// <summary>Claims due workflow instances atomically so that many workers can poll the same table safely.</summary>
public interface IWorkflowStore
{
    Task<IReadOnlyList<Guid>> ClaimDueAsync(string owner, TimeSpan lease, int maxCount, CancellationToken ct);

    Task ExtendLeaseAsync(Guid instanceId, string owner, TimeSpan lease, CancellationToken ct);
}

/// <summary>Bounded per-tenant concurrency so one tenant's load cannot monopolise the worker pool.</summary>
public interface ITenantWorkLimiter
{
    Task<IDisposable> AcquireAsync(TenantId tenantId, string workKind, int limit, CancellationToken ct);
}
