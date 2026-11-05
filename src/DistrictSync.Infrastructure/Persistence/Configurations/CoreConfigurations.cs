using System.Text.Json;
using DistrictSync.Application.Common;
using DistrictSync.Domain.Common;
using DistrictSync.Domain.Ingestion;
using DistrictSync.Domain.Sources;
using DistrictSync.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DistrictSync.Infrastructure.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    /// <summary>SQL Server rowversion for optimistic concurrency.</summary>
    public static void HasRowVersion<T>(this EntityTypeBuilder<T> builder) where T : class =>
        builder.Property<byte[]?>("RowVersion").IsRowVersion();

    /// <summary>Stores a list value object as a JSON string column (provider-neutral, no owned-entity ceremony).</summary>
    public static PropertyBuilder<List<TItem>> HasJsonListConversion<TItem>(this PropertyBuilder<List<TItem>> property) =>
        property.HasConversion(
            new ValueConverter<List<TItem>, string>(
                v => JsonSerializer.Serialize(v, JsonDefaults.Options),
                v => JsonSerializer.Deserialize<List<TItem>>(v, JsonDefaults.Options) ?? new List<TItem>()),
            new ValueComparer<List<TItem>>(
                (a, b) => JsonSerializer.Serialize(a, JsonDefaults.Options) == JsonSerializer.Serialize(b, JsonDefaults.Options),
                v => JsonSerializer.Serialize(v, JsonDefaults.Options).GetHashCode(StringComparison.Ordinal),
                v => JsonSerializer.Deserialize<List<TItem>>(JsonSerializer.Serialize(v, JsonDefaults.Options), JsonDefaults.Options)!))
            .HasColumnType("nvarchar(max)");
}

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(200);
        builder.Property(t => t.Slug).HasMaxLength(64);
        builder.HasIndex(t => t.Slug).IsUnique();
        builder.HasIndex(t => t.TenantId).IsUnique();
        builder.OwnsOne(t => t.Quotas, q =>
        {
            q.Property(x => x.MaxConcurrentReconciliations).HasColumnName("MaxConcurrentReconciliations");
            q.Property(x => x.MaxConcurrentIngestions).HasColumnName("MaxConcurrentIngestions");
            q.Property(x => x.IngestionRequestsPerMinute).HasColumnName("IngestionRequestsPerMinute");
        });
        builder.Navigation(t => t.Quotas).IsRequired();
        builder.HasRowVersion();
        builder.Ignore(t => t.DomainEvents);
    }
}

public sealed class SourceSystemConfiguration : IEntityTypeConfiguration<SourceSystem>
{
    public void Configure(EntityTypeBuilder<SourceSystem> builder)
    {
        builder.ToTable("SourceSystems");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(200);
        builder.Property(s => s.Code).HasMaxLength(32);
        builder.Property(s => s.ConfigJson).HasColumnType("nvarchar(max)");
        builder.Property(s => s.ApiKeyHash).HasMaxLength(64);
        builder.HasIndex(s => new { s.TenantId, s.Code }).IsUnique();
        builder.HasRowVersion();
        builder.Ignore(s => s.DomainEvents);
    }
}

public sealed class IngestionBatchConfiguration : IEntityTypeConfiguration<IngestionBatch>
{
    public void Configure(EntityTypeBuilder<IngestionBatch> builder)
    {
        builder.ToTable("IngestionBatches");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EntityType).HasMaxLength(64);
        builder.Property(x => x.StagingReference).HasMaxLength(1024);
        builder.Property(x => x.ContentHash).HasMaxLength(64);
        builder.Property(x => x.FileName).HasMaxLength(260);
        builder.Property(x => x.FailureReason).HasMaxLength(2000);
        builder.Property(x => x.SubmittedBy).HasMaxLength(200);
        builder.HasIndex(x => new { x.TenantId, x.SourceSystemId, x.ContentHash });
        builder.HasIndex(x => new { x.TenantId, x.Status, x.ReceivedAt });
        builder.HasRowVersion();
        builder.Ignore(x => x.DomainEvents);
    }
}

public sealed class SourceRecordConfiguration : IEntityTypeConfiguration<SourceRecord>
{
    public void Configure(EntityTypeBuilder<SourceRecord> builder)
    {
        builder.ToTable("SourceRecords");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.EntityType).HasMaxLength(64);
        builder.Property(r => r.ExternalId).HasMaxLength(128);

        // Binary collation: SQL Server ORDER BY on this column must agree with string.CompareOrdinal in the
        // merge-join. The default case-insensitive collation would order "a-b" and "ab" differently from .NET.
        builder.Property(r => r.MatchKey).HasMaxLength(256).UseCollation("Latin1_General_BIN2");
        builder.Property(r => r.PayloadJson).HasColumnType("nvarchar(max)");
        builder.Property(r => r.PayloadHash).HasMaxLength(64);
        builder.Property(r => r.RowVersion).IsRowVersion();

        // The upsert lookup key.
        builder.HasIndex(r => new { r.TenantId, r.SourceSystemId, r.EntityType, r.ExternalId }).IsUnique();

        // The streaming read path: ordered keyset pagination per (tenant, source, entity type).
        builder.HasIndex(r => new { r.TenantId, r.SourceSystemId, r.EntityType, r.MatchKey, r.ExternalId })
            .HasFilter("[IsDeleted] = 0")
            .IncludeProperties(r => new { r.SourceUpdatedAt });
    }
}
