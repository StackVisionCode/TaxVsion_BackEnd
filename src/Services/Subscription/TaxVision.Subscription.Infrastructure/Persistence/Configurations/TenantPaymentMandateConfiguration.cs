using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Billing;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class TenantPaymentMandateConfiguration : IEntityTypeConfiguration<TenantPaymentMandate>
{
    public void Configure(EntityTypeBuilder<TenantPaymentMandate> builder)
    {
        builder.ToTable("TenantPaymentMandates");
        builder.HasKey(mandate => mandate.Id);

        // Id lo asigna la factory de dominio y la entidad cuelga de TenantBillingPreference._mandates.
        builder.Property(mandate => mandate.Id).ValueGeneratedNever();

        builder.Property(mandate => mandate.TenantBillingPreferenceId).IsRequired();
        builder.Property(mandate => mandate.TenantId).IsRequired();
        builder.Property(mandate => mandate.ProviderCode).HasMaxLength(50).IsRequired();
        builder.Property(mandate => mandate.ConsentText).HasMaxLength(4000).IsRequired();
        builder.Property(mandate => mandate.ConsentTextVersion).HasMaxLength(50).IsRequired();
        builder.Property(mandate => mandate.ConsentIpAddress).HasMaxLength(64).IsRequired();
        builder.Property(mandate => mandate.ConsentUserAgent).HasMaxLength(500);
        builder.Property(mandate => mandate.AcceptedByUserId).IsRequired();
        builder.Property(mandate => mandate.AcceptedAtUtc).IsRequired();
        builder.Property(mandate => mandate.AgreedAmountCents).IsRequired();
        builder.Property(mandate => mandate.AgreedCurrency).HasMaxLength(10).IsRequired();
        builder.Property(mandate => mandate.AgreedCadence).HasMaxLength(20).IsRequired();
        builder.Property(mandate => mandate.IsRevoked).IsRequired();
        builder.Property(mandate => mandate.RevokedReason).HasMaxLength(500);
        builder.Property(mandate => mandate.CreatedAtUtc).IsRequired();

        builder
            .HasIndex(mandate => new { mandate.TenantId, mandate.IsRevoked })
            .HasDatabaseName("IX_TenantPaymentMandates_Tenant_IsRevoked");
    }
}
