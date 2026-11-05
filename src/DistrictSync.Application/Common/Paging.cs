using DistrictSync.Contracts.Api;
using Microsoft.EntityFrameworkCore;

namespace DistrictSync.Application.Common;

public readonly record struct PageRequest(int Page, int PageSize)
{
    public const int MaxPageSize = 200;

    public static PageRequest Normalize(int? page, int? pageSize) => new(Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 25, 1, MaxPageSize));

    public int Skip => (Page - 1) * PageSize;
}

public static class PagingExtensions
{
    public static async Task<PagedResult<TDto>> ToPagedResultAsync<TEntity, TDto>(this IQueryable<TEntity> query, PageRequest page, Func<TEntity, TDto> map, CancellationToken ct)
    {
        var total = await query.LongCountAsync(ct);
        var items = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<TDto>(items.Select(map).ToList(), page.Page, page.PageSize, total);
    }
}
