using DistrictSync.Application.Abstractions;
using DistrictSync.Domain.Common;
using DistrictSync.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace DistrictSync.Application.Rules;

public interface IActiveRuleSetProvider
{
    /// <summary>The published rule set for the entity type, falling back to the tenant's "*" set. Null when none is published.</summary>
    Task<CompiledRuleSet?> GetActiveAsync(TenantId tenantId, string entityType, CancellationToken ct);

    Task InvalidateAsync(TenantId tenantId, string entityType, CancellationToken ct);
}

/// <summary>
/// Reads through a two-level cache (in-process + Redis). Publishing a rule set emits an event that
/// every worker consumes to invalidate, so rule changes take effect across the fleet within seconds
/// and without a redeploy.
/// </summary>
public sealed class ActiveRuleSetProvider(IAppDbContext db, HybridCache cache) : IActiveRuleSetProvider
{
    private static readonly HybridCacheEntryOptions EntryOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromMinutes(1),
    };

    public async Task<CompiledRuleSet?> GetActiveAsync(TenantId tenantId, string entityType, CancellationToken ct)
    {
        var key = CacheKey(tenantId, entityType);
        var snapshot = await cache.GetOrCreateAsync(
            key,
            (db, tenantId, entityType),
            static async (state, token) => await LoadAsync(state.db, state.tenantId, state.entityType, token),
            EntryOptions,
            tags: [$"rules:{tenantId}"],
            cancellationToken: ct);

        return snapshot.RuleSet;
    }

    public async Task InvalidateAsync(TenantId tenantId, string entityType, CancellationToken ct)
    {
        await cache.RemoveAsync(CacheKey(tenantId, entityType), ct);
        await cache.RemoveAsync(CacheKey(tenantId, "*"), ct);
        await cache.RemoveByTagAsync($"rules:{tenantId}", ct);
    }

    private static string CacheKey(TenantId tenantId, string entityType) => $"rules:{tenantId}:{entityType.ToUpperInvariant()}";

    private static async Task<RuleSetSnapshot> LoadAsync(IAppDbContext db, TenantId tenantId, string entityType, CancellationToken ct)
    {
        var candidates = await db.RuleSets
            .IgnoreQueryFilters()
            .Include(r => r.Rules)
            .Where(r => r.TenantId == tenantId && r.Status == RuleSetStatus.Published && (r.EntityType == entityType || r.EntityType == "*"))
            .ToListAsync(ct);

        var chosen = candidates
            .OrderByDescending(r => r.EntityType == entityType)
            .ThenByDescending(r => r.Version)
            .FirstOrDefault();

        return new RuleSetSnapshot(chosen is null ? null : CompiledRuleSet.From(chosen));
    }

    /// <summary>Wrapper so a "nothing published" result is cacheable too (HybridCache cannot store null directly).</summary>
    private sealed record RuleSetSnapshot(CompiledRuleSet? RuleSet);
}
