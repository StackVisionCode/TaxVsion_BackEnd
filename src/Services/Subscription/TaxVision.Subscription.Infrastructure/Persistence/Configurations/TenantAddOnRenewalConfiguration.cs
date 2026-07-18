using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.AddOns;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class TenantAddOnRenewalConfiguration : IEntityTypeConfiguration<TenantAddOnRenewal>
{
    public void Configure(EntityTypeBuilder<TenantAddOnRenewal> builder)
    {
        builder.ToTable("TenantAddOnRenewals");
        builder.HasKey(renewal => renewal.Id);

        // *** GUARDRAIL persistencia (§49) ***
        // Id se genera en la factory de dominio y la entidad cuelga de
        // TenantAddOn._renewals (HasMany) -> requiere ValueGeneratedNever().
        builder.Property(renewal => renewal.Id).ValueGeneratedNever();

        builder.Property(renewal => renewal.TenantAddOnId).IsRequired();
        builder.Property(renewal => renewal.TenantId).IsRequired();
        builder.Property(renewal => renewal.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(renewal => renewal.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(renewal => renewal.FailureCode).HasMaxLength(100);
        builder.Property(renewal => renewal.FailureReason).HasMaxLength(1000);
        builder.Property(renewal => renewal.ExternalPaymentReference).HasMaxLength(200);

        builder
            .HasIndex(renewal => renewal.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_TenantAddOnRenewals_IdempotencyKey");

        builder
            .HasIndex(renewal => new { renewal.Status, renewal.NextRetryAtUtc })
            .HasDatabaseName("IX_TenantAddOnRenewals_Status_NextRetry");
    }
}
