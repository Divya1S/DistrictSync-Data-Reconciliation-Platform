using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Ingestion;

namespace DistrictSync.UnitTests.Ingestion;

public class RecordNormalizerTests
{
    private static readonly SourceConfig Config = new(
        ExternalIdField: "employee_id",
        MatchKeyField: "work_email",
        UpdatedAtField: "last_modified",
        DeletedField: "status",
        FieldMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["employee_id"] = "employeeId", ["work_email"] = "email", ["first_name"] = "firstName" },
        IgnoreFields: ["internal_note"]);

    private static RawRecord Raw(params (string Key, string? Value)[] fields) =>
        new(null, fields.ToDictionary(f => f.Key, f => f.Value, StringComparer.OrdinalIgnoreCase), null, false, 7);

    [Fact]
    public void Maps_fields_normalizes_match_key_and_hashes_canonically()
    {
        var ok = RecordNormalizer.TryNormalize(Raw(("employee_id", " E-100 "), ("work_email", "  MAILTO:Ann.Lee@Northgate.EDU "), ("first_name", "Ann   Marie"), ("internal_note", "x"), ("last_modified", "2026-10-01T10:00:00Z")), Config, out var normalized, out var rejected);

        ok.ShouldBeTrue();
        rejected.ShouldBeNull();
        normalized.ShouldNotBeNull();
        normalized.ExternalId.ShouldBe("E-100");
        normalized.MatchKey.ShouldBe("ann.lee@northgate.edu");
        normalized.Attributes["firstName"].ShouldBe("Ann Marie");
        normalized.Attributes.ContainsKey("internal_note").ShouldBeFalse();
        normalized.UpdatedAt.ShouldBe(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));
        normalized.LineNumber.ShouldBe(7);
        normalized.PayloadHash.Length.ShouldBe(64);
    }

    [Fact]
    public void Hash_is_independent_of_attribute_order()
    {
        RecordNormalizer.TryNormalize(Raw(("employee_id", "1"), ("work_email", "a@b.c"), ("first_name", "A"), ("dept", "X")), Config, out var first, out _);
        RecordNormalizer.TryNormalize(Raw(("dept", "X"), ("first_name", "A"), ("work_email", "a@b.c"), ("employee_id", "1")), Config, out var second, out _);
        first!.PayloadHash.ShouldBe(second!.PayloadHash);
        first.PayloadJson.ShouldBe(second.PayloadJson);
    }

    [Fact]
    public void Rejects_missing_external_id_and_match_key()
    {
        RecordNormalizer.TryNormalize(Raw(("work_email", "a@b.c")), Config, out _, out var noId).ShouldBeFalse();
        noId!.Reason.ShouldContain("employee_id");

        RecordNormalizer.TryNormalize(Raw(("employee_id", "1"), ("work_email", "   ")), Config, out _, out var noKey).ShouldBeFalse();
        noKey!.ExternalId.ShouldBe("1");
        noKey.Reason.ShouldContain("work_email");
    }

    [Fact]
    public void Rejects_unparseable_timestamp()
    {
        RecordNormalizer.TryNormalize(Raw(("employee_id", "1"), ("work_email", "a@b.c"), ("last_modified", "yesterday")), Config, out _, out var rejected).ShouldBeFalse();
        rejected!.Reason.ShouldContain("yesterday");
    }

    [Theory]
    [InlineData("Inactive", true)]
    [InlineData("true", true)]
    [InlineData("Active", false)]
    [InlineData(null, false)]
    public void Reads_deleted_flag_from_configured_field(string? status, bool expectedDeleted)
    {
        RecordNormalizer.TryNormalize(Raw(("employee_id", "1"), ("work_email", "a@b.c"), ("status", status)), Config, out var normalized, out _).ShouldBeTrue();
        normalized!.Deleted.ShouldBe(expectedDeleted);
    }

    [Fact]
    public void Empty_values_become_null()
    {
        RecordNormalizer.TryNormalize(Raw(("employee_id", "1"), ("work_email", "a@b.c"), ("first_name", "   ")), Config, out var normalized, out _).ShouldBeTrue();
        normalized!.Attributes["firstName"].ShouldBeNull();
    }

    [Fact]
    public void Source_config_round_trips_through_json()
    {
        var json = Config.ToJson();
        var parsed = SourceConfig.Parse(json);
        parsed.ShouldBe(Config with { FieldMap = parsed.FieldMap, IgnoreFields = parsed.IgnoreFields });
        parsed.FieldMap!["work_email"].ShouldBe("email");
        SourceConfig.Parse("{}").ShouldBe(new SourceConfig());
        SourceConfig.Parse(null).ShouldBe(new SourceConfig());
    }
}
