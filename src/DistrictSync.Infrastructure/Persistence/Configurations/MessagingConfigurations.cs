using DistrictSync.Application.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistrictSync.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(128);
        builder.Property(m => m.Payload).HasColumnType("nvarchar(max)");
        builder.Property(m => m.TraceParent).HasMaxLength(64);
        builder.Property(m => m.LastError).HasMaxLength(2000);
        builder.Property(m => m.LockOwner).HasMaxLength(100);

        // Dispatcher polls only unprocessed rows; a filtered index keeps that cheap no matter how big the history grows.
        builder.HasIndex(m => new { m.OccurredAt }).HasFilter("[ProcessedAt] IS NULL").HasDatabaseName("IX_Outbox_Pending");
        builder.HasIndex(m => m.ProcessedAt);
    }
}

public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("InboxMessages");
        builder.HasKey(m => new { m.MessageId, m.Consumer });
        builder.Property(m => m.Consumer).HasMaxLength(100);
        builder.Property(m => m.Type).HasMaxLength(128);
        builder.HasIndex(m => m.FirstReceivedAt);
        builder.Property(m => m.LeaseExpiresAt);
    }
}

public sealed class DeadLetterMessageConfiguration : IEntityTypeConfiguration<DeadLetterMessage>
{
    public void Configure(EntityTypeBuilder<DeadLetterMessage> builder)
    {
        builder.ToTable("DeadLetterMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Queue).HasMaxLength(128);
        builder.Property(m => m.RoutingKey).HasMaxLength(128);
        builder.Property(m => m.MessageType).HasMaxLength(128);
        builder.Property(m => m.Payload).HasColumnType("nvarchar(max)");
        builder.Property(m => m.Reason).HasMaxLength(4000);
        builder.Property(m => m.ReplayedBy).HasMaxLength(200);
        builder.HasIndex(m => new { m.TenantId, m.ReplayedAt, m.DeadLetteredAt });
    }
}
