using DistrictSync.Application.Ingestion;
using DistrictSync.Application.Tenancy;
using DistrictSync.Domain.Reconciliation;
using DistrictSync.Domain.Rules;
using DistrictSync.Domain.Sources;
using DistrictSync.Domain.Tenants;
using DistrictSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DistrictSync.Infrastructure.Seeding;

/// <summary>
/// Creates the demo tenant "Northgate Unified School District" with three source systems, a comparison
/// profile and a published rule set, so the stack is usable the moment it starts. Idempotent.
/// </summary>
public sealed class DemoDataSeeder(AppDbContext db, TenantContext tenant, TimeProvider clock, ILogger<DemoDataSeeder> logger)
{
    public const string DemoTenantSlug = "northgate";

    public async Task SeedAsync(CancellationToken ct)
    {
        tenant.SetSystem("seeder");
        if (await db.Tenants.AnyAsync(t => t.Slug == DemoTenantSlug, ct))
        {
            return;
        }

        var now = clock.GetUtcNow();
        var demo = Tenant.Create("Northgate Unified School District", DemoTenantSlug, now, new TenantQuotas(2, 4, 600));
        db.Tenants.Add(demo);
        await db.SaveChangesAsync(ct);

        tenant.SetSystemForTenant(demo.TenantId, "seeder");

        var employeeConfig = new SourceConfig(
            ExternalIdField: "employee_id",
            MatchKeyField: "work_email",
            UpdatedAtField: "last_modified",
            DeletedField: null,
            LoadMode: LoadMode.Snapshot,
            FieldMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["employee_id"] = "employeeId",
                ["work_email"] = "email",
                ["first_name"] = "firstName",
                ["last_name"] = "lastName",
                ["department"] = "department",
                ["job_title"] = "title",
                ["annual_salary"] = "salary",
                ["phone"] = "phone",
                ["status"] = "status",
                ["hire_date"] = "hireDate",
                ["last_modified"] = "lastModified",
            });

        var hr = SourceSystem.Create(demo.TenantId, "Workday HR", "HR", SourceSystemKind.CsvUpload, 90, employeeConfig.ToJson(), now);
        var payroll = SourceSystem.Create(demo.TenantId, "Payroll ERP", "PAYROLL", SourceSystemKind.CsvUpload, 70, employeeConfig.ToJson(), now);
        var directory = SourceSystem.Create(demo.TenantId, "Staff Directory", "DIRECTORY", SourceSystemKind.Webhook, 40, employeeConfig.ToJson(), now);
        directory.RotateApiKey(Application.Common.Hashing.Sha256Hex("dsk_demo-directory-webhook-key"));
        db.SourceSystems.AddRange(hr, payroll, directory);

        var profile = ComparisonProfile.Create(demo.TenantId, "Employee core fields", "Employee",
        [
            new ComparedField("firstName", FieldNormalizer.WhitespaceCollapse, false),
            new ComparedField("lastName", FieldNormalizer.WhitespaceCollapse, false),
            new ComparedField("department", FieldNormalizer.CaseInsensitive, false),
            new ComparedField("title", FieldNormalizer.CaseInsensitive, false),
            new ComparedField("salary", FieldNormalizer.Decimal, true),
            new ComparedField("phone", FieldNormalizer.PhoneDigits, false),
            new ComparedField("status", FieldNormalizer.CaseInsensitive, true),
            new ComparedField("hireDate", FieldNormalizer.DateOnly, false),
        ], isDefault: true, now);
        db.ComparisonProfiles.Add(profile);

        var rules = RuleSet.CreateDraft(demo.TenantId, "Employee reconciliation policy", "Employee", now);
        rules.AddRule(
            "Ignore terminated staff missing from payroll",
            "discrepancy.type == \"MissingInRight\" and left.status == \"Terminated\"",
            RuleAction.Ignore("Terminated employees are expected to be absent from payroll."),
            "HR is the system of record for employment status; payroll drops terminated staff.");
        rules.AddRule(
            "Salary or status conflicts need payroll approval",
            "discrepancy.type == \"FieldConflict\" and fields.any(\"salary\", \"status\")",
            RuleAction.OpenCase(CasePriority.High, assignToRole: "payroll", note: "Compensation or employment status differs; requires payroll review."),
            "Money and employment status are never auto-corrected.");
        rules.AddRule(
            "Contact details: newest wins",
            "discrepancy.type == \"FieldConflict\" and fields.only(\"phone\", \"title\", \"department\")",
            RuleAction.AutoResolve(ResolutionStrategy.PreferNewest, "Low-risk contact/org fields follow the most recent update."),
            "Phone, title and department changes are safe to propagate automatically.");
        rules.AddRule(
            "Name differences: trust HR",
            "discrepancy.type == \"FieldConflict\" and fields.only(\"firstName\", \"lastName\")",
            RuleAction.AutoResolve(ResolutionStrategy.PreferHigherTrust, "Legal names follow HR."),
            "HR holds the legal name.");
        rules.AddRule(
            "New hires missing from payroll are urgent",
            "discrepancy.type == \"MissingInRight\" and left.status == \"Active\" and daysSince(left.hireDate) <= 30",
            RuleAction.OpenCase(CasePriority.Urgent, assignToRole: "payroll", note: "Recently hired employee is not set up in payroll."),
            "Nobody should miss their first paycheck.");
        rules.AddRule(
            "Duplicates need a human",
            "discrepancy.type == \"Duplicate\"",
            RuleAction.OpenCase(CasePriority.Normal, assignToRole: "data-steward"),
            "Choosing the surviving record is a judgement call.");
        rules.Publish("seeder", "Initial policy", now);
        db.RuleSets.Add(rules);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo tenant {Slug} ({TenantId})", DemoTenantSlug, demo.TenantId);
    }
}
