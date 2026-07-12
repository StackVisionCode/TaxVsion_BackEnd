using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Audit;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionAuditLogConfiguration : IEntityTypeConfiguration<SubscriptionAuditLog>
{
    public void Configure(EntityTypeBuilder<SubscriptionAuditLog> builder)
    {
        builder.ToTable("SubscriptionAuditLogs");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.TenantId).IsRequired();
        builder.Property(entry => entry.AggregateType).HasMaxLength(50).IsRequired();
        builder.Property(entry => entry.AggregateId).IsRequired();
        builder.Property(entry => entry.Action).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.ActorUserId).IsRequired();
        builder.Property(entry => entry.ActorType).HasMaxLength(30).IsRequired();
        builder.Property(entry => entry.OccurredAtUtc).IsRequired();
        builder.Property(entry => entry.CorrelationId).HasMaxLength(200);
        builder.Property(entry => entry.BeforePayload).HasColumnType("nvarchar(max)");
        builder.Property(entry => entry.AfterPayload).HasColumnType("nvarchar(max)");
        builder.Property(entry => entry.Reason).HasMaxLength(500);

        builder.HasIndex(entry => new { entry.TenantId, entry.OccurredAtUtc })
            .HasDatabaseName("IX_SubscriptionAuditLogs_TenantId_OccurredAtUtc");

        builder.HasIndex(entry => new { entry.AggregateType, entry.AggregateId })
            .HasDatabaseName("IX_SubscriptionAuditLogs_AggregateType_AggregateId");
    }
}
