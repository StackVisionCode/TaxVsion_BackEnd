using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentClient.Domain.Audit;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Configurations;

public sealed class PaymentAuditEntryConfiguration : IEntityTypeConfiguration<PaymentAuditEntry>
{
    public void Configure(EntityTypeBuilder<PaymentAuditEntry> builder)
    {
        builder.ToTable("PaymentAuditEntries");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.TenantId).IsRequired();
        builder.Property(entry => entry.AggregateType).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.AggregateId).IsRequired();
        builder.Property(entry => entry.Action).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(entry => entry.ActorUserId).IsRequired();
        builder.Property(entry => entry.ActorType).HasMaxLength(30).IsRequired();
        builder.Property(entry => entry.OccurredAtUtc).IsRequired();
        builder.Property(entry => entry.CorrelationId).HasMaxLength(100);
        builder.Property(entry => entry.BeforePayload).HasColumnType("nvarchar(max)");
        builder.Property(entry => entry.AfterPayload).HasColumnType("nvarchar(max)");
        builder.Property(entry => entry.Reason).HasMaxLength(500);

        builder
            .HasIndex(entry => new
            {
                entry.TenantId,
                entry.AggregateType,
                entry.AggregateId,
            })
            .HasDatabaseName("IX_PaymentAuditEntries_TenantId_Aggregate");
    }
}
