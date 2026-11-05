using DistrictSync.Api.Auth;
using DistrictSync.Application.Tenancy;
using DistrictSync.Domain.Common;

namespace DistrictSync.Api.Middleware;

/// <summary>Projects the authenticated principal onto the scoped <see cref="TenantContext"/> so every query below is tenant-bound.</summary>
public sealed class TenantMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenant)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true && TenantId.TryParse(user.FindFirst(ClaimNames.TenantId)?.Value, out var tenantId))
        {
            var roles = user.FindAll(ClaimNames.Role).Select(c => c.Value).ToArray();
            tenant.Set(tenantId, user.FindFirst(ClaimNames.Subject)?.Value ?? "unknown", roles);
            context.Items["TenantId"] = tenantId;
        }

        await next(context);
    }
}
