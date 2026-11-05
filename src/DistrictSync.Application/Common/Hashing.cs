using System.Security.Cryptography;
using System.Text;

namespace DistrictSync.Application.Common;

public static class Hashing
{
    public static string Sha256Hex(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static async Task<string> Sha256HexAsync(Stream stream, CancellationToken ct)
    {
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Generates a 256-bit URL-safe API key. Returned once to the caller; only the hash is persisted.</summary>
    public static string NewApiKey() => "dsk_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
