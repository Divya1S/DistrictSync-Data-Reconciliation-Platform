using DistrictSync.Application.Common;

namespace DistrictSync.Api.Endpoints;

internal static class EndpointHelpers
{
    public static PageRequest Page(int? page, int? pageSize) => PageRequest.Normalize(page, pageSize);

    public static TEnum? ParseEnum<TEnum>(string? value, string parameterName) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Enum.TryParse<TEnum>(value, true, out var parsed))
        {
            throw ValidationException.For(parameterName, $"'{value}' is not a valid {typeof(TEnum).Name}. Expected one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
        }

        return parsed;
    }

    public static string? IfMatch(HttpRequest request) => request.Headers.IfMatch.FirstOrDefault();

    public static void SetETag(HttpResponse response, string etag)
    {
        if (!string.IsNullOrEmpty(etag))
        {
            response.Headers.ETag = $"\"{etag}\"";
        }
    }
}
