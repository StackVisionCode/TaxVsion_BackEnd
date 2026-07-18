using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class TenantSubscriptionRenewalConfiguration : IEntityTypeConfiguration<TenantSubscriptionRenewal>
{
    public void Configure(EntityTypeBuilder<TenantSubscriptionRenewal> builder)
    {
        builder.ToTable("TenantSubscriptionRenewals");
        builder.HasKey(renewal => renewal.Id);

        // *** GUARDRAIL persistencia (§49) ***
        // Id se genera en la factory de dominio y la entidad cuelga de
        // TenantSubscription._renewals (HasMany) -> requiere ValueGeneratedNever().
        builder.Property(renewal => renewal.Id).ValueGeneratedNever();

        builder.Property(renewal => renewal.TenantSubscriptionId).IsRequired();
        builder.Property(renewal => renewal.TenantId).IsRequired();
        builder.Property(renewal => renewal.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(renewal => renewal.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(renewal => renewal.FailureCode).HasMaxLength(100);
        builder.Property(renewal => renewal.FailureReason).HasMaxLength(1000);
        builder.Property(renewal => renewal.ExternalPaymentReference).HasMaxLength(200);

        builder
            .HasIndex(renewal => renewal.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_TenantSubscriptionRenewals_IdempotencyKey");

        builder
            .HasIndex(renewal => new { renewal.Status, renewal.NextRetryAtUtc })
            .HasDatabaseName("IX_TenantSubscriptionRenewals_Status_NextRetry");
    }
}
