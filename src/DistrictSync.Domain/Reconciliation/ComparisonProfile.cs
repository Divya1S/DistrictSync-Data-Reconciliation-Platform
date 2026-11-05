using DistrictSync.Domain.Common;

namespace DistrictSync.Domain.Reconciliation;

/// <summary>How a field is normalised before two values are compared.</summary>
public enum FieldNormalizer
{
    None = 0,
    Trim = 1,
    CaseInsensitive = 2,
    Email = 3,
    PhoneDigits = 4,
    DateOnly = 5,
    Decimal = 6,
    WhitespaceCollapse = 7,
}

public sealed record ComparedField(string Name, FieldNormalizer Normalizer, bool IsCritical);

/// <summary>
/// Declares which fields of an entity type are compared and how. Profiles are tenant-owned so each
/// organisation decides what "the same employee" means for its data.
/// </summary>
public sealed class ComparisonProfile : AggregateRoot, ITenantScoped
{
    private readonly List<ComparedField> _fields = [];

    private ComparisonProfile() { }

    public TenantId TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public IReadOnlyCollection<ComparedField> Fields => _fields.AsReadOnly();
    public bool IsDefault { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static ComparisonProfile Create(TenantId tenantId, string name, string entityType, IEnumerable<ComparedField> fields, bool isDefault, DateTimeOffset now)
    {
        var list = fields.ToList();
        if (list.Count == 0)
        {
            throw new DomainException("no_fields", "A comparison profile needs at least one field.");
        }

        if (list.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Count)
        {
            throw new DomainException("duplicate_fields", "Field names must be unique within a profile.");
        }

        var profile = new ComparisonProfile
        {
            TenantId = tenantId,
            Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name), 200),
            EntityType = Guard.MaxLength(Guard.NotNullOrWhiteSpace(entityType), 64),
            IsDefault = isDefault,
            CreatedAt = now,
        };
        profile._fields.AddRange(list);
        return profile;
    }

    public void MakeDefault() => IsDefault = true;

    public void ClearDefault() => IsDefault = false;
}
