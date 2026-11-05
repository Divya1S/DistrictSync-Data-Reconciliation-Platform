namespace DistrictSync.Application.Common;

public static class ETag
{
    public static string From(byte[]? rowVersion) => rowVersion is null ? string.Empty : Convert.ToBase64String(rowVersion);

    /// <summary>Validates an optional caller-supplied ETag (from If-Match) against the row's current version.</summary>
    public static void EnsureMatches(string? expected, byte[]? rowVersion, string resource, object key)
    {
        if (!string.IsNullOrEmpty(expected) && !string.Equals(expected.Trim('"'), From(rowVersion), StringComparison.Ordinal))
        {
            throw new ConcurrencyConflictException(resource, key);
        }
    }
}
