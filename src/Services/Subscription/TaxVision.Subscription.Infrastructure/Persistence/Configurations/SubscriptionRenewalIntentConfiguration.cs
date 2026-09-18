using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionRenewalIntentConfiguration : IEntityTypeConfiguration<SubscriptionRenewalIntent>
{
    public void Configure(EntityTypeBuilder<SubscriptionRenewalIntent> builder)
    {
        builder.ToTable("SubscriptionRenewalIntents");
        builder.HasKey(intent => intent.Id);

        builder.Property(intent => intent.TenantId).IsRequired();
        builder.Property(intent => intent.AmountCents).IsRequired();
        builder.Property(intent => intent.Currency).HasMaxLength(3).IsRequired();
        builder.Property(intent => intent.BillingCycle).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(intent => intent.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(intent => intent.SaaSPaymentId);
        builder.Property(intent => intent.CheckoutUrl).HasMaxLength(2048);
        builder.Property(intent => intent.RequestedByUserId).IsRequired();
        builder.Property(intent => intent.CreatedAtUtc).IsRequired();
        builder.Property(intent => intent.UpdatedAtUtc).IsRequired();
        builder.Property(intent => intent.PaidAtUtc);
        builder.Property(intent => intent.FailureReason).HasMaxLength(1000);

        builder.HasIndex(intent => intent.TenantId);
        builder.HasIndex(intent => intent.SaaSPaymentId);
    }
}
