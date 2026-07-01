using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Payment.Domain.SaaSPayments;

namespace TaxVision.Payment.Infrastructure.Persistence.Configurations;

public sealed class SaaSPaymentConfiguration : IEntityTypeConfiguration<SaaSPayment>
{
    public void Configure(EntityTypeBuilder<SaaSPayment> builder)
    {
        builder.ToTable("SaaSPayments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.PaymentType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(p => p.AmountCents).IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(10).IsRequired();
        builder.Property(p => p.StripePaymentIntentId).HasMaxLength(255);
        builder.Property(p => p.ReferenceId).IsRequired();
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();
        builder.Property(p => p.FailureReason).HasMaxLength(1000);
        builder.HasIndex(p => new { p.TenantId, p.ReferenceId, p.PaymentType });
        builder.HasIndex(p => p.StripePaymentIntentId)
            .HasFilter("[StripePaymentIntentId] IS NOT NULL");
    }
}
