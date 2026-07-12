using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionSeatRenewalConfiguration : IEntityTypeConfiguration<SubscriptionSeatRenewal>
{
    public void Configure(EntityTypeBuilder<SubscriptionSeatRenewal> builder)
    {
        builder.ToTable("SubscriptionSeatRenewals");
        builder.HasKey(renewal => renewal.Id);

        // *** GUARDRAIL persistencia (§49) ***
        // Id se genera en la factory de dominio y la entidad cuelga de
        // SubscriptionSeat._renewals (HasMany) -> requiere ValueGeneratedNever().
        builder.Property(renewal => renewal.Id).ValueGeneratedNever();

        builder.Property(renewal => renewal.SeatId).IsRequired();
        builder.Property(renewal => renewal.TenantId).IsRequired();
        builder.Property(renewal => renewal.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(renewal => renewal.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(renewal => renewal.FailureCode).HasMaxLength(100);
        builder.Property(renewal => renewal.FailureReason).HasMaxLength(1000);
        builder.Property(renewal => renewal.ExternalPaymentReference).HasMaxLength(200);

        builder.HasIndex(renewal => renewal.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_SubscriptionSeatRenewals_IdempotencyKey");

        builder.HasIndex(renewal => new { renewal.Status, renewal.NextRetryAtUtc })
            .HasDatabaseName("IX_SubscriptionSeatRenewals_Status_NextRetry");
    }
}
