using DistrictSync.Application.Reconciliation;
using DistrictSync.Domain.Reconciliation;

namespace DistrictSync.UnitTests.Reconciliation;

public class FieldComparerTests
{
    [Theory]
    [InlineData("(555) 010-1234", FieldNormalizer.PhoneDigits, "5550101234")]
    [InlineData("+1 555 010 1234", FieldNormalizer.PhoneDigits, "5550101234")]
    [InlineData("$61,500.00", FieldNormalizer.Decimal, "61500")]
    [InlineData("61500.5000", FieldNormalizer.Decimal, "61500.5")]
    [InlineData("2026-10-20T14:35:00Z", FieldNormalizer.DateOnly, "2026-10-20")]
    [InlineData("  Finance  ", FieldNormalizer.CaseInsensitive, "FINANCE")]
    [InlineData("Ann   Marie", FieldNormalizer.WhitespaceCollapse, "ANN MARIE")]
    [InlineData("Ann.Lee@X.com", FieldNormalizer.Email, "ann.lee@x.com")]
    [InlineData("  ", FieldNormalizer.Trim, null)]
    public void Normalizes_values(string input, FieldNormalizer normalizer, string? expected) =>
        FieldNormalization.Normalize(input, normalizer).ShouldBe(expected);

    [Fact]
    public void Profile_compares_only_listed_fields_and_flags_critical()
    {
        var comparer = new FieldComparer(
        [
            new ComparedField("salary", FieldNormalizer.Decimal, true),
            new ComparedField("phone", FieldNormalizer.PhoneDigits, false),
            new ComparedField("title", FieldNormalizer.CaseInsensitive, false),
        ]);

        var differences = comparer.Compare(
            """{"salary":"61,500","phone":"(555) 010-1234","title":"Teacher","ignored":"a"}""",
            """{"salary":"60000","phone":"555-010-1234","title":"TEACHER","ignored":"b"}""");

        differences.Count.ShouldBe(1);
        differences[0].Field.ShouldBe("salary");
        differences[0].IsCritical.ShouldBeTrue();
        differences[0].LeftValue.ShouldBe("61,500");
        differences[0].RightValue.ShouldBe("60000");
    }

    [Fact]
    public void Compare_all_uses_union_of_fields_with_trim_semantics()
    {
        var differences = FieldComparer.CompareAll.Compare("""{"a":"1","b":" x "}""", """{"a":"1","b":"x","c":"new"}""");
        differences.Select(d => d.Field).ShouldBe(["c"]);
        differences[0].LeftValue.ShouldBeNull();
        differences[0].RightValue.ShouldBe("new");
    }

    [Fact]
    public void Malformed_payload_is_treated_as_empty()
    {
        var differences = FieldComparer.CompareAll.Compare("not json", """{"a":"1"}""");
        differences.Count.ShouldBe(1);
    }
}
