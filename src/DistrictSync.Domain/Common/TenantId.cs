namespace DistrictSync.Domain.Common;

/// <summary>
/// Strongly-typed tenant identifier. Using a distinct type (rather than a bare Guid) makes it
/// impossible to accidentally pass a record id where a tenant id is expected: the compiler enforces
/// the isolation boundary that the database query filters enforce at runtime.
/// </summary>
public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.CreateVersion7());

    public static TenantId Parse(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out TenantId tenantId)
    {
        if (Guid.TryParse(value, out var guid))
        {
            tenantId = new TenantId(guid);
            return true;
        }

        tenantId = default;
        return false;
    }

    public override string ToString() => Value.ToString("D");
}
