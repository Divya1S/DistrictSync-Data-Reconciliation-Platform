using System.Text;

namespace DistrictSync.Infrastructure.Messaging;

internal static class MessageHeaders
{
    public const string TenantId = "x-tenant-id";
    public const string Attempt = "x-attempt";
    public const string TraceParent = "traceparent";
    public const string OriginQueue = "x-origin-queue";
    public const string DeadReason = "x-dead-reason";
    public const string OriginalRoutingKey = "x-original-routing-key";

    public static string? GetString(IDictionary<string, object?>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string s => s,
            _ => value.ToString(),
        };
    }

    public static int GetInt(IDictionary<string, object?>? headers, string key, int fallback = 0)
    {
        if (headers is null || !headers.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        return value switch
        {
            int i => i,
            long l => (int)l,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => fallback,
        };
    }
}
