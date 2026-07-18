using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentClient.Domain.Payouts;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Configurations;

public sealed class PayoutScheduleItemConfiguration : IEntityTypeConfiguration<PayoutScheduleItem>
{
    public void Configure(EntityTypeBuilder<PayoutScheduleItem> builder)
    {
        builder.ToTable("PayoutScheduleItems");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();

        builder.Property(item => item.PayoutScheduleId).IsRequired();
        builder.Property(item => item.TenantId).IsRequired();
        builder.Property(item => item.ProviderPayoutReference).HasMaxLength(255).IsRequired();

        builder.OwnsOne(
            item => item.Amount,
            money =>
            {
                money.Property(m => m.AmountCents).HasColumnName("AmountCents").IsRequired();
                money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
            }
        );

        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(item => item.FailureReason).HasMaxLength(1000);
        builder.Property(item => item.OccurredAtUtc).IsRequired();

        builder
            .HasIndex(item => new { item.PayoutScheduleId, item.ProviderPayoutReference })
            .IsUnique()
            .HasDatabaseName("UX_PayoutScheduleItems_ScheduleId_ProviderReference");
    }
}
