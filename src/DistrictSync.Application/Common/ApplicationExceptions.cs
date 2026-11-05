namespace DistrictSync.Application.Common;

/// <summary>The requested resource does not exist (or belongs to another tenant, which must look identical to the caller).</summary>
public sealed class NotFoundException(string resource, object key) : Exception($"{resource} '{key}' was not found.")
{
    public string Resource { get; } = resource;
    public object Key { get; } = key;
}

/// <summary>The caller's ETag no longer matches the row: someone else changed it first. Maps to HTTP 412.</summary>
public sealed class ConcurrencyConflictException(string resource, object key) : Exception($"{resource} '{key}' was modified by another request. Reload and retry.")
{
    public string Resource { get; } = resource;
    public object Key { get; } = key;
}

/// <summary>Input failed validation. Maps to HTTP 400 with field errors.</summary>
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception("One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public static ValidationException For(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>The caller lacks a role required by the operation. Maps to HTTP 403.</summary>
public sealed class ForbiddenException(string message) : Exception(message);
