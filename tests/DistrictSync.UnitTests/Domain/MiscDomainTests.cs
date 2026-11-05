using DistrictSync.Domain.Common;
using DistrictSync.Domain.Reconciliation;
using DistrictSync.Domain.Tenants;

namespace DistrictSync.UnitTests.Domain;

public class MiscDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("northgate", true)]
    [InlineData("north-gate-2", true)]
    [InlineData("North Gate", false)]
    [InlineData("north_gate", false)]
    public void Tenant_slug_is_validated(string slug, bool valid)
    {
        if (valid)
        {
            Tenant.Create("Northgate", slug, Now).Slug.ShouldBe(slug);
        }
        else
        {
            Should.Throw<DomainException>(() => Tenant.Create("Northgate", slug, Now)).Code.ShouldBe("invalid_slug");
        }
    }

    [Fact]
    public void TenantId_parses_and_prints_round_trip()
    {
        var id = TenantId.New();
        TenantId.TryParse(id.ToString(), out var parsed).ShouldBeTrue();
        parsed.ShouldBe(id);
        TenantId.TryParse("nope", out _).ShouldBeFalse();
        TenantId.TryParse(null, out _).ShouldBeFalse();
    }

    [Fact]
    public void Discrepancy_severity_depends_on_type_and_critical_fields()
    {
        var tenant = TenantId.New();
        Discrepancy.Detect(tenant, Guid.NewGuid(), "E", DiscrepancyType.FieldConflict, "k", Guid.NewGuid(), Guid.NewGuid(), null, null, [new FieldDifference("phone", "1", "2", false)], Now).Severity.ShouldBe(Severity.Low);
        Discrepancy.Detect(tenant, Guid.NewGuid(), "E", DiscrepancyType.FieldConflict, "k", Guid.NewGuid(), Guid.NewGuid(), null, null, [new FieldDifference("salary", "1", "2", true)], Now).Severity.ShouldBe(Severity.High);
        Discrepancy.Detect(tenant, Guid.NewGuid(), "E", DiscrepancyType.MissingInRight, "k", Guid.NewGuid(), Guid.NewGuid(), null, null, [], Now).Severity.ShouldBe(Severity.Medium);
    }

    [Fact]
    public void Discrepancy_status_transitions_are_guarded()
    {
        var d = Discrepancy.Detect(TenantId.New(), Guid.NewGuid(), "E", DiscrepancyType.FieldConflict, "k", Guid.NewGuid(), Guid.NewGuid(), null, null, [], Now);
        d.EscalateToCase(null);
        d.Status.ShouldBe(DiscrepancyStatus.CaseOpened);
        Should.Throw<InvalidStateTransitionException>(() => d.AutoResolve(Guid.NewGuid(), "x", Now));
        d.Resolve("done", Now);
        d.Status.ShouldBe(DiscrepancyStatus.Resolved);
        Should.Throw<InvalidStateTransitionException>(() => d.Resolve("again", Now));
    }

    [Fact]
    public void Reconciliation_run_requires_two_different_sources()
    {
        var source = Guid.NewGuid();
        Should.Throw<DomainException>(() => ReconciliationRun.Request(TenantId.New(), "E", source, source, null, "alice", Now)).Code.ShouldBe("same_source");
    }

    [Fact]
    public void Comparison_profile_rejects_duplicate_fields()
    {
        Should.Throw<DomainException>(() => ComparisonProfile.Create(TenantId.New(), "p", "E",
            [new ComparedField("a", FieldNormalizer.Trim, false), new ComparedField("A", FieldNormalizer.Trim, false)], false, Now)).Code.ShouldBe("duplicate_fields");
    }
}
