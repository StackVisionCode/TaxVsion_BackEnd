using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentApp.Domain.Billing;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Configurations;

public sealed class TenantBillingPreferenceProjectionConfiguration
    : IEntityTypeConfiguration<TenantBillingPreferenceProjection>
{
    public void Configure(EntityTypeBuilder<TenantBillingPreferenceProjection> builder)
    {
        builder.ToTable("TenantBillingPreferenceProjections");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.Mode).HasMaxLength(20).IsRequired();
        builder.Property(p => p.ProviderCode).HasMaxLength(50);
        builder.Property(p => p.HasActiveMandate).IsRequired();
        builder.Property(p => p.AgreedAmountCents);
        builder.Property(p => p.AgreedCurrency).HasMaxLength(10);
        builder.Property(p => p.AgreedCadence).HasMaxLength(20);
        builder.Property(p => p.RevisionTicks).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        builder.HasIndex(p => p.TenantId).IsUnique();
    }
}
