using DistrictSync.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistrictSync.Infrastructure.Persistence.Configurations;

public sealed class ReconciliationRunConfiguration : IEntityTypeConfiguration<ReconciliationRun>
{
    public void Configure(EntityTypeBuilder<ReconciliationRun> builder)
    {
        builder.ToTable("ReconciliationRuns");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.EntityType).HasMaxLength(64);
        builder.Property(r => r.FailureReason).HasMaxLength(2000);
        builder.Property(r => r.RequestedBy).HasMaxLength(200);
        builder.Property(r => r.Checkpoint).HasMaxLength(256).UseCollation("Latin1_General_BIN2");
        builder.OwnsOne(r => r.Stats, s =>
        {
            s.Property(x => x.LeftRecords).HasColumnName("Stats_LeftRecords");
            s.Property(x => x.RightRecords).HasColumnName("Stats_RightRecords");
            s.Property(x => x.Matched).HasColumnName("Stats_Matched");
            s.Property(x => x.MissingInLeft).HasColumnName("Stats_MissingInLeft");
            s.Property(x => x.MissingInRight).HasColumnName("Stats_MissingInRight");
            s.Property(x => x.FieldConflicts).HasColumnName("Stats_FieldConflicts");
            s.Property(x => x.Duplicates).HasColumnName("Stats_Duplicates");
            s.Property(x => x.AutoResolved).HasColumnName("Stats_AutoResolved");
            s.Property(x => x.CasesOpened).HasColumnName("Stats_CasesOpened");
            s.Property(x => x.Ignored).HasColumnName("Stats_Ignored");
        });
        builder.Navigation(r => r.Stats).IsRequired();
        builder.HasIndex(r => new { r.TenantId, r.Status, r.RequestedAt });
        builder.HasRowVersion();
        builder.Ignore(r => r.DomainEvents);
    }
}

public sealed class ComparisonProfileConfiguration : IEntityTypeConfiguration<ComparisonProfile>
{
    public void Configure(EntityTypeBuilder<ComparisonProfile> builder)
    {
        builder.ToTable("ComparisonProfiles");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(200);
        builder.Property(p => p.EntityType).HasMaxLength(64);
        builder.Property<List<ComparedField>>("_fields").HasColumnName("FieldsJson").HasJsonListConversion();
        builder.Ignore(p => p.Fields);
        builder.HasIndex(p => new { p.TenantId, p.EntityType, p.IsDefault });
        builder.HasRowVersion();
        builder.Ignore(p => p.DomainEvents);
    }
}

public sealed class DiscrepancyConfiguration : IEntityTypeConfiguration<Discrepancy>
{
    public void Configure(EntityTypeBuilder<Discrepancy> builder)
    {
        builder.ToTable("Discrepancies");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.EntityType).HasMaxLength(64);
        builder.Property(d => d.MatchKey).HasMaxLength(256).UseCollation("Latin1_General_BIN2");
        builder.Property(d => d.ResolutionNote).HasMaxLength(1000);
        builder.Property<List<FieldDifference>>("_differences").HasColumnName("DifferencesJson").HasJsonListConversion();
        builder.Ignore(d => d.Differences);
        builder.HasIndex(d => new { d.TenantId, d.RunId, d.Status });
        builder.HasIndex(d => new { d.TenantId, d.Status, d.Severity });
        builder.HasIndex(d => new { d.TenantId, d.DetectedAt });
        builder.HasRowVersion();
        builder.Ignore(d => d.DomainEvents);
    }
}
