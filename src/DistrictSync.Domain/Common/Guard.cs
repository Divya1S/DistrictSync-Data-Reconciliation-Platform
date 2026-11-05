using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace DistrictSync.Domain.Common;

public static class Guard
{
    public static string NotNullOrWhiteSpace([NotNull] string? value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("required", $"{name} is required.");
        }

        return value;
    }

    public static string MaxLength(string value, int max, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value.Length > max)
        {
            throw new DomainException("too_long", $"{name} must be at most {max} characters.");
        }

        return value;
    }

    public static int InRange(int value, int min, int max, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value < min || value > max)
        {
            throw new DomainException("out_of_range", $"{name} must be between {min} and {max}.");
        }

        return value;
    }

    public static Guid NotEmpty(Guid value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("required", $"{name} is required.");
        }

        return value;
    }
}
