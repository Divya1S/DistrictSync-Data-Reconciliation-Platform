namespace DistrictSync.Api.Auth;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    public string Issuer { get; set; } = "districtsync";

    public string Audience { get; set; } = "districtsync-api";

    /// <summary>HS256 signing key. The default is for local development only; production must supply its own via secrets.</summary>
    public string SigningKey { get; set; } = "districtsync-development-signing-key-change-me-0123456789";

    /// <summary>Enables POST /api/v1/auth/dev-token. Never enable outside development.</summary>
    public bool EnableDevTokens { get; set; }

    public int TokenLifetimeHours { get; set; } = 8;
}

public static class ClaimNames
{
    public const string Subject = "sub";
    public const string TenantId = "tenant_id";
    public const string TenantSlug = "tenant_slug";
    public const string Role = "role";
}
