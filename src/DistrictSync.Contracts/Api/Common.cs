namespace DistrictSync.Contracts.Api;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record IdResponse(Guid Id);

public sealed record DevTokenRequest(string TenantSlug, string UserId, string[] Roles);

public sealed record DevTokenResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid TenantId);

public sealed record TenantDto(Guid Id, string Name, string Slug, bool IsActive, int MaxConcurrentReconciliations, int MaxConcurrentIngestions, int IngestionRequestsPerMinute, DateTimeOffset CreatedAt);

public sealed record CreateTenantRequest(string Name, string Slug);
