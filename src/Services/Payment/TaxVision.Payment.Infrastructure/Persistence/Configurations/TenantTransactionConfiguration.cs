using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Payment.Domain.TenantPayments;

namespace TaxVision.Payment.Infrastructure.Persistence.Configurations;

public sealed class TenantTransactionConfiguration : IEntityTypeConfiguration<TenantTransaction>
{
    public void Configure(EntityTypeBuilder<TenantTransaction> builder)
    {
        builder.ToTable("TenantTransactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TenantId).IsRequired();
        builder.Property(t => t.CustomerId);
        builder.Property(t => t.Provider)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(t => t.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(t => t.AmountCents).IsRequired();
        builder.Property(t => t.Currency).HasMaxLength(10).IsRequired();
        builder.Property(t => t.ExternalTransactionId).HasMaxLength(500);
        builder.Property(t => t.Description).HasMaxLength(500).IsRequired();
        builder.Property(t => t.CreatedAtUtc).IsRequired();
        builder.Property(t => t.UpdatedAtUtc).IsRequired();
        builder.Property(t => t.FailureReason).HasMaxLength(1000);
        builder.HasIndex(t => new { t.TenantId, t.CreatedAtUtc });
        builder.HasIndex(t => new { t.TenantId, t.CustomerId })
            .HasFilter("[CustomerId] IS NOT NULL");
    }
}
