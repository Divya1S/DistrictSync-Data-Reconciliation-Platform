using DistrictSync.Domain.Cases;
using DistrictSync.Domain.Rules;
using DistrictSync.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistrictSync.Infrastructure.Persistence.Configurations;

public sealed class RuleSetConfiguration : IEntityTypeConfiguration<RuleSet>
{
    public void Configure(EntityTypeBuilder<RuleSet> builder)
    {
        builder.ToTable("RuleSets");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(200);
        builder.Property(r => r.EntityType).HasMaxLength(64);
        builder.Property(r => r.PublishedBy).HasMaxLength(200);
        builder.Property(r => r.ChangeNote).HasMaxLength(1000);
        builder.HasMany(r => r.Rules).WithOne().HasForeignKey(x => x.RuleSetId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Rules).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_rules");
        builder.HasIndex(r => new { r.TenantId, r.FamilyId, r.Version }).IsUnique();
        builder.HasIndex(r => new { r.TenantId, r.EntityType, r.Status });
        builder.HasRowVersion();
        builder.Ignore(r => r.DomainEvents);
    }
}

public sealed class RuleConfiguration : IEntityTypeConfiguration<Rule>
{
    public void Configure(EntityTypeBuilder<Rule> builder)
    {
        builder.ToTable("Rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(200);
        builder.Property(r => r.Description).HasMaxLength(1000);
        builder.Property(r => r.Condition).HasMaxLength(4000);
        builder.OwnsOne(r => r.Action, a =>
        {
            a.Property(x => x.Kind).HasColumnName("ActionKind").HasConversion<string>().HasMaxLength(32);
            a.Property(x => x.Strategy).HasColumnName("ActionStrategy").HasConversion<string>().HasMaxLength(32);
            a.Property(x => x.Priority).HasColumnName("ActionPriority").HasConversion<string>().HasMaxLength(32);
            a.Property(x => x.AssignToRole).HasColumnName("ActionAssignToRole").HasMaxLength(64);
            a.Property(x => x.Note).HasColumnName("ActionNote").HasMaxLength(1000);
        });
        builder.Navigation(r => r.Action).IsRequired();
        builder.HasIndex(r => new { r.RuleSetId, r.Order });
    }
}

public sealed class ResolutionCaseConfiguration : IEntityTypeConfiguration<ResolutionCase>
{
    public void Configure(EntityTypeBuilder<ResolutionCase> builder)
    {
        builder.ToTable("Cases");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Title).HasMaxLength(300);
        builder.Property(c => c.AssignedRole).HasMaxLength(64);
        builder.Property(c => c.AssigneeId).HasMaxLength(200);
        builder.Property(c => c.ApprovedBy).HasMaxLength(200);
        builder.OwnsOne(c => c.Proposal, p =>
        {
            p.Property(x => x.Strategy).HasColumnName("ProposalStrategy").HasConversion<string>().HasMaxLength(32);
            p.Property(x => x.ManualValuesJson).HasColumnName("ProposalManualValuesJson").HasColumnType("nvarchar(max)");
            p.Property(x => x.Note).HasColumnName("ProposalNote").HasMaxLength(2000);
            p.Property(x => x.ProposedBy).HasColumnName("ProposalProposedBy").HasMaxLength(200);
            p.Property(x => x.ProposedAt).HasColumnName("ProposalProposedAt");
        });
        builder.HasMany(c => c.History).WithOne().HasForeignKey(h => h.CaseId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.History).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_history");
        builder.HasIndex(c => new { c.TenantId, c.State, c.DueAt });
        builder.HasIndex(c => new { c.TenantId, c.DiscrepancyId }).IsUnique();
        builder.HasIndex(c => new { c.TenantId, c.AssigneeId });
        builder.HasRowVersion();
        builder.Ignore(c => c.DomainEvents);
        builder.Ignore(c => c.IsTerminal);
    }
}

public sealed class CaseHistoryEntryConfiguration : IEntityTypeConfiguration<CaseHistoryEntry>
{
    public void Configure(EntityTypeBuilder<CaseHistoryEntry> builder)
    {
        builder.ToTable("CaseHistory");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedOnAdd();
        builder.Property(h => h.Actor).HasMaxLength(200);
        builder.Property(h => h.Action).HasMaxLength(32);
        builder.Property(h => h.FromState).HasMaxLength(32);
        builder.Property(h => h.ToState).HasMaxLength(32);
        builder.Property(h => h.Note).HasMaxLength(2000);
        builder.HasIndex(h => new { h.CaseId, h.At });
    }
}

public sealed class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> builder)
    {
        builder.ToTable("WorkflowInstances");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.WorkflowType).HasMaxLength(100);
        builder.Property(w => w.StateJson).HasColumnType("nvarchar(max)");
        builder.Property(w => w.LockOwner).HasMaxLength(100);
        builder.Property(w => w.LastError).HasMaxLength(4000);
        builder.Property(w => w.RowVersion).IsRowVersion();

        // The runner's polling predicate.
        builder.HasIndex(w => new { w.Status, w.WakeUpAt }).HasFilter("[Status] IN ('Scheduled','Running')");
        builder.HasIndex(w => new { w.TenantId, w.WorkflowType, w.SubjectId });
    }
}
