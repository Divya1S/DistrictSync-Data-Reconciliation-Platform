namespace DistrictSync.Contracts.Api;

public sealed record SourceSystemDto(Guid Id, string Name, string Code, string Kind, int TrustRank, string ConfigJson, bool IsEnabled, bool HasApiKey, DateTimeOffset CreatedAt);

public sealed record CreateSourceSystemRequest(string Name, string Code, string Kind, int TrustRank, string? ConfigJson);

/// <summary>The API key is returned exactly once, at creation or rotation; only its hash is stored.</summary>
public sealed record SourceSystemCreatedResponse(SourceSystemDto SourceSystem, string? ApiKey);

public sealed record ApiKeyResponse(string ApiKey);
