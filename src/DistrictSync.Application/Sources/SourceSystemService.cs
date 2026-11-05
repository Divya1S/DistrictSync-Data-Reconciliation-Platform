using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Common;
using DistrictSync.Application.Ingestion;
using DistrictSync.Contracts.Api;
using DistrictSync.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace DistrictSync.Application.Sources;

public sealed class SourceSystemService(IAppDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public async Task<IReadOnlyList<SourceSystemDto>> ListAsync(CancellationToken ct) =>
        (await db.SourceSystems.AsNoTracking().OrderBy(s => s.Code).ToListAsync(ct)).Select(Mappers.ToDto).ToList();

    public async Task<SourceSystemDto> GetAsync(Guid id, CancellationToken ct) =>
        Mappers.ToDto(await db.SourceSystems.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("SourceSystem", id));

    public async Task<SourceSystemCreatedResponse> CreateAsync(CreateSourceSystemRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<SourceSystemKind>(request.Kind, true, out var kind))
        {
            throw ValidationException.For("kind", $"Unknown kind '{request.Kind}'. Expected RestApi, CsvUpload or Webhook.");
        }

        var config = SourceConfig.Parse(request.ConfigJson);
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.SourceSystems.AnyAsync(s => s.Code == code, ct))
        {
            throw ValidationException.For("code", $"A source system with code '{code}' already exists.");
        }

        var source = SourceSystem.Create(tenant.TenantId, request.Name, code, kind, request.TrustRank, config.ToJson(), clock.GetUtcNow());
        string? apiKey = null;
        if (kind == SourceSystemKind.Webhook)
        {
            apiKey = Hashing.NewApiKey();
            source.RotateApiKey(Hashing.Sha256Hex(apiKey));
        }

        db.SourceSystems.Add(source);
        await db.SaveChangesAsync(ct);
        return new SourceSystemCreatedResponse(Mappers.ToDto(source), apiKey);
    }

    public async Task<ApiKeyResponse> RotateApiKeyAsync(Guid id, CancellationToken ct)
    {
        var source = await db.SourceSystems.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("SourceSystem", id);
        var apiKey = Hashing.NewApiKey();
        source.RotateApiKey(Hashing.Sha256Hex(apiKey));
        await db.SaveChangesAsync(ct);
        return new ApiKeyResponse(apiKey);
    }

    /// <summary>Resolves the source for a webhook call by (id, api key). Constant-time hash comparison; cross-tenant lookups are impossible because the key is bound to the source row.</summary>
    public async Task<SourceSystem?> AuthenticateWebhookAsync(Guid sourceSystemId, string apiKey, CancellationToken ct)
    {
        var source = await db.SourceSystems.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == sourceSystemId && s.IsEnabled && s.Kind == SourceSystemKind.Webhook, ct);
        if (source?.ApiKeyHash is null)
        {
            return null;
        }

        var presented = Hashing.Sha256Hex(apiKey);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(presented),
            System.Text.Encoding.UTF8.GetBytes(source.ApiKeyHash))
            ? source
            : null;
    }
}
