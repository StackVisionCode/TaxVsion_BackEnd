using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class SeatPurchaseIntentConfiguration : IEntityTypeConfiguration<SeatPurchaseIntent>
{
    public void Configure(EntityTypeBuilder<SeatPurchaseIntent> builder)
    {
        builder.ToTable("SeatPurchaseIntents");
        builder.HasKey(intent => intent.Id);

        builder.Property(intent => intent.TenantId).IsRequired();
        builder.Property(intent => intent.SeatType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(intent => intent.Quantity).IsRequired();
        builder.Property(intent => intent.AutoRenew).IsRequired();
        builder.Property(intent => intent.BillingCycle).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(intent => intent.ProratedTotalCents).IsRequired();
        builder.Property(intent => intent.Currency).HasMaxLength(3).IsRequired();
        builder.Property(intent => intent.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(intent => intent.SaaSPaymentId);
        builder.Property(intent => intent.CheckoutUrl).HasMaxLength(2048);
        builder.Property(intent => intent.RequestedByUserId).IsRequired();
        builder.Property(intent => intent.CreatedAtUtc).IsRequired();
        builder.Property(intent => intent.UpdatedAtUtc).IsRequired();
        builder.Property(intent => intent.PaidAtUtc);
        builder.Property(intent => intent.FailureReason).HasMaxLength(1000);

        builder.OwnsOne(
            intent => intent.UnitPrice,
            money =>
            {
                money.Property(m => m.Amount).HasColumnName("UnitPriceAmount").HasPrecision(18, 4).IsRequired();
                money.Property(m => m.Currency).HasColumnName("UnitPriceCurrency").HasMaxLength(3).IsRequired();
            }
        );

        builder.HasIndex(intent => intent.TenantId);
        builder.HasIndex(intent => intent.SaaSPaymentId);
    }
}
