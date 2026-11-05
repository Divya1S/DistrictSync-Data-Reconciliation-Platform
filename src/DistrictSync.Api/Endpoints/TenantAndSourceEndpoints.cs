using DistrictSync.Api.Auth;
using DistrictSync.Application.Sources;
using DistrictSync.Application.Tenants;
using DistrictSync.Contracts.Api;
using Microsoft.Extensions.Options;

namespace DistrictSync.Api.Endpoints;

public static class TenantAndSourceEndpoints
{
    public static RouteGroupBuilder MapTenants(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/tenants").WithTags("Tenants").RequireAuthorization();

        group.MapGet("/me", async (TenantService tenants, CancellationToken ct) => Results.Ok(await tenants.GetCurrentAsync(ct)));

        // Platform-level operation: only exposed when dev tokens are enabled (no platform admin IdP in this reference implementation).
        group.MapPost("/", async (CreateTenantRequest request, TenantService tenants, IOptions<AuthOptions> options, CancellationToken ct) =>
        {
            if (!options.Value.EnableDevTokens)
            {
                return Results.NotFound();
            }

            var tenant = await tenants.CreateAsync(request, ct);
            return Results.Created($"/api/v1/tenants/{tenant.TenantId}", Application.Common.Mappers.ToDto(tenant));
        }).RequireAuthorization("admin");

        return api;
    }

    public static RouteGroupBuilder MapSourceSystems(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/source-systems").WithTags("Source systems").RequireAuthorization();

        group.MapGet("/", async (SourceSystemService sources, CancellationToken ct) => Results.Ok(await sources.ListAsync(ct)));
        group.MapGet("/{id:guid}", async (Guid id, SourceSystemService sources, CancellationToken ct) => Results.Ok(await sources.GetAsync(id, ct)));

        group.MapPost("/", async (CreateSourceSystemRequest request, SourceSystemService sources, CancellationToken ct) =>
        {
            var created = await sources.CreateAsync(request, ct);
            return Results.Created($"/api/v1/source-systems/{created.SourceSystem.Id}", created);
        }).RequireAuthorization("admin");

        group.MapPost("/{id:guid}/rotate-key", async (Guid id, SourceSystemService sources, CancellationToken ct) => Results.Ok(await sources.RotateApiKeyAsync(id, ct)))
            .RequireAuthorization("admin");

        return api;
    }
}
