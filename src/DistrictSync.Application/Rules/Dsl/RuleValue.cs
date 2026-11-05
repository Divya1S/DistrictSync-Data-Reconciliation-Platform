using System.Globalization;
using System.Text.Json.Nodes;

namespace DistrictSync.Application.Rules.Dsl;

public enum RuleValueKind
{
    Null,
    Bool,
    Number,
    String,
    List,
    Object,
}

/// <summary>
/// Dynamically-typed value produced while evaluating a rule. Comparisons are deliberately lenient
/// (numeric strings compare as numbers, string equality ignores case) because the facts come from
/// heterogeneous source systems that disagree about formatting far more often than about meaning.
/// </summary>
public readonly struct RuleValue : IEquatable<RuleValue>
{
    private readonly object? _value;

    private RuleValue(RuleValueKind kind, object? value)
    {
        Kind = kind;
        _value = value;
    }

    public RuleValueKind Kind { get; }

    public static RuleValue Null { get; } = new(RuleValueKind.Null, null);
    public static RuleValue True { get; } = new(RuleValueKind.Bool, true);
    public static RuleValue False { get; } = new(RuleValueKind.Bool, false);

    public static RuleValue From(bool value) => value ? True : False;
    public static RuleValue From(decimal value) => new(RuleValueKind.Number, value);
    public static RuleValue From(string? value) => value is null ? Null : new(RuleValueKind.String, value);
    public static RuleValue From(IReadOnlyList<RuleValue> items) => new(RuleValueKind.List, items);
    public static RuleValue FromObject(JsonObject obj) => new(RuleValueKind.Object, obj);

    public static RuleValue FromJson(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return Null;
            case JsonArray array:
                return From(array.Select(FromJson).ToList());
            case JsonObject obj:
                return FromObject(obj);
            case JsonValue value:
                if (value.TryGetValue<bool>(out var b))
                {
                    return From(b);
                }

                if (value.TryGetValue<decimal>(out var d))
                {
                    return From(d);
                }

                if (value.TryGetValue<double>(out var dbl))
                {
                    return From((decimal)dbl);
                }

                if (value.TryGetValue<string>(out var s))
                {
                    return From(s);
                }

                return From(value.ToJsonString());
            default:
                return Null;
        }
    }

    public bool IsNull => Kind == RuleValueKind.Null;

    public bool IsTruthy => Kind switch
    {
        RuleValueKind.Null => false,
        RuleValueKind.Bool => (bool)_value!,
        RuleValueKind.Number => (decimal)_value! != 0,
        RuleValueKind.String => ((string)_value!).Length > 0,
        RuleValueKind.List => ((IReadOnlyList<RuleValue>)_value!).Count > 0,
        _ => true,
    };

    public IReadOnlyList<RuleValue> AsList() => Kind == RuleValueKind.List ? (IReadOnlyList<RuleValue>)_value! : IsNull ? [] : [this];

    public JsonObject? AsObject() => Kind == RuleValueKind.Object ? (JsonObject)_value! : null;

    public bool TryAsNumber(out decimal number)
    {
        switch (Kind)
        {
            case RuleValueKind.Number:
                number = (decimal)_value!;
                return true;
            case RuleValueKind.Bool:
                number = (bool)_value! ? 1 : 0;
                return true;
            case RuleValueKind.String:
                return decimal.TryParse((string)_value!, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out number);
            default:
                number = 0;
                return false;
        }
    }

    public bool TryAsDateTime(out DateTimeOffset value)
    {
        if (Kind == RuleValueKind.String && DateTimeOffset.TryParse((string)_value!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    public string AsString() => Kind switch
    {
        RuleValueKind.Null => string.Empty,
        RuleValueKind.Bool => (bool)_value! ? "true" : "false",
        RuleValueKind.Number => ((decimal)_value!).ToString(CultureInfo.InvariantCulture),
        RuleValueKind.String => (string)_value!,
        RuleValueKind.List => string.Join(",", ((IReadOnlyList<RuleValue>)_value!).Select(v => v.AsString())),
        _ => ((JsonObject)_value!).ToJsonString(),
    };

    /// <summary>Loose equality used by <c>==</c>, <c>in</c> and <c>contains</c>.</summary>
    public static bool LooseEquals(RuleValue a, RuleValue b)
    {
        if (a.IsNull || b.IsNull)
        {
            return a.IsNull && b.IsNull;
        }

        if (a.Kind == RuleValueKind.Bool || b.Kind == RuleValueKind.Bool)
        {
            return a.IsTruthy == b.IsTruthy && a.Kind != RuleValueKind.List && b.Kind != RuleValueKind.List;
        }

        if (a.TryAsNumber(out var na) && b.TryAsNumber(out var nb))
        {
            return na == nb;
        }

        if (a.Kind == RuleValueKind.List || b.Kind == RuleValueKind.List)
        {
            var la = a.AsList();
            var lb = b.AsList();
            return la.Count == lb.Count && la.Zip(lb).All(p => LooseEquals(p.First, p.Second));
        }

        return string.Equals(a.AsString().Trim(), b.AsString().Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ordering used by relational operators. Numbers numerically, ISO dates chronologically, otherwise ordinal text.</summary>
    public static int? Compare(RuleValue a, RuleValue b)
    {
        if (a.IsNull || b.IsNull)
        {
            return null;
        }

        if (a.TryAsNumber(out var na) && b.TryAsNumber(out var nb))
        {
            return na.CompareTo(nb);
        }

        if (a.TryAsDateTime(out var da) && b.TryAsDateTime(out var db))
        {
            return da.CompareTo(db);
        }

        return string.Compare(a.AsString(), b.AsString(), StringComparison.OrdinalIgnoreCase);
    }

    public bool Equals(RuleValue other) => LooseEquals(this, other);

    public override bool Equals(object? obj) => obj is RuleValue other && Equals(other);

    public override int GetHashCode() => AsString().ToUpperInvariant().GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Kind == RuleValueKind.String ? $"\"{AsString()}\"" : AsString();

    public static bool operator ==(RuleValue left, RuleValue right) => left.Equals(right);

    public static bool operator !=(RuleValue left, RuleValue right) => !left.Equals(right);
}
