using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Growth.Infrastructure.Persistence.Audit;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations;

public sealed class GrowthAuditEntryConfiguration : IEntityTypeConfiguration<GrowthAuditEntry>
{
    public void Configure(EntityTypeBuilder<GrowthAuditEntry> builder)
    {
        builder.ToTable("AuditEntries", GrowthSchemas.Audit);
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.TenantId).IsRequired();
        builder.Property(entry => entry.BoundedContext).HasMaxLength(30).IsRequired();
        builder.Property(entry => entry.AggregateType).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.AggregateId).IsRequired();
        builder.Property(entry => entry.Action).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.ActorId).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.ActorType).HasMaxLength(30).IsRequired();
        builder.Property(entry => entry.OccurredAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(entry => entry.CorrelationId).HasMaxLength(100);
        builder.Property(entry => entry.CausationId).HasMaxLength(100);
        builder.Property(entry => entry.TraceId).HasMaxLength(100);
        builder.Property(entry => entry.BeforeJson).HasColumnType("nvarchar(max)");
        builder.Property(entry => entry.AfterJson).HasColumnType("nvarchar(max)");
        builder.Property(entry => entry.Reason).HasMaxLength(500);

        builder
            .HasIndex(entry => new { entry.TenantId, entry.OccurredAtUtc })
            .HasDatabaseName("IX_AuditEntries_TenantId_OccurredAtUtc");
        builder
            .HasIndex(entry => new
            {
                entry.AggregateType,
                entry.AggregateId,
                entry.OccurredAtUtc,
            })
            .HasDatabaseName("IX_AuditEntries_Aggregate_OccurredAtUtc");
    }
}
