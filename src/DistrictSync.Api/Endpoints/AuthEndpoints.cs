using DistrictSync.Api.Auth;
using DistrictSync.Application.Common;
using DistrictSync.Application.Tenants;
using DistrictSync.Contracts.Api;
using Microsoft.Extensions.Options;

namespace DistrictSync.Api.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/dev-token", async (DevTokenRequest request, IOptions<AuthOptions> options, DevTokenIssuer issuer, TenantService tenants, CancellationToken ct) =>
        {
            if (!options.Value.EnableDevTokens)
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(request.UserId))
            {
                throw ValidationException.For("userId", "A user id is required.");
            }

            var tenant = await tenants.FindBySlugAsync(request.TenantSlug, ct) ?? throw new NotFoundException("Tenant", request.TenantSlug);
            var roles = request.Roles is { Length: > 0 } ? request.Roles : ["viewer"];
            var (token, expires) = issuer.Issue(tenant, request.UserId, roles);
            return Results.Ok(new DevTokenResponse(token, expires, tenant.TenantId.Value));
        })
        .AllowAnonymous()
        .WithSummary("Issue a development JWT for a tenant/user/roles (development environments only).");

        return api;
    }
}
