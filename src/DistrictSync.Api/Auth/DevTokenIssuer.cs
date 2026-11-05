using System.Text;
using DistrictSync.Domain.Tenants;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DistrictSync.Api.Auth;

/// <summary>
/// Mints JWTs for local development and demos. In production, tokens come from the organisation's
/// identity provider (Entra ID, Okta...) and this class is not registered.
/// </summary>
public sealed class DevTokenIssuer(IOptions<AuthOptions> options, TimeProvider clock)
{
    public (string Token, DateTimeOffset ExpiresAt) Issue(Tenant tenant, string userId, IReadOnlyCollection<string> roles)
    {
        var o = options.Value;
        var expires = clock.GetUtcNow().AddHours(o.TokenLifetimeHours);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            IssuedAt = clock.GetUtcNow().UtcDateTime,
            NotBefore = clock.GetUtcNow().UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [ClaimNames.Subject] = userId,
                [ClaimNames.TenantId] = tenant.TenantId.ToString(),
                [ClaimNames.TenantSlug] = tenant.Slug,
                [ClaimNames.Role] = roles.Select(r => r.ToLowerInvariant()).Distinct().ToArray(),
            },
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
