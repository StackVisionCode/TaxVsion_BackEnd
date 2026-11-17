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
        // Money sale de estas dos columnas: el dominio las expone juntas, el esquema no cambia.
        builder.Property<long>("AmountCents").IsRequired();
        builder.Property<string>("Currency").HasMaxLength(3).IsRequired();
        builder.Property(intent => intent.BillingCycle).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(intent => intent.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(intent => intent.SaaSPaymentId);
        builder.Property(intent => intent.CheckoutUrl).HasMaxLength(2048);
        builder.Property(intent => intent.CheckoutProvider).HasMaxLength(30);
        builder.Property(intent => intent.CheckoutExpiresAtUtc);
        builder.Property(intent => intent.RequestedByUserId).IsRequired();
        builder.Property(intent => intent.CreatedAtUtc).IsRequired();
        builder.Property(intent => intent.UpdatedAtUtc).IsRequired();
        builder.Property(intent => intent.PaidAtUtc);
        builder.Property(intent => intent.CancelledAtUtc);
        builder.Property(intent => intent.ExpiredAtUtc);
        builder.Property(intent => intent.CancelledByUserId);
        builder.Property(intent => intent.CancelReason).HasMaxLength(500);
        builder.Property(intent => intent.FailureReason).HasMaxLength(1000);
        builder.Property(intent => intent.RowVersion).IsRowVersion();

        builder.Property(intent => intent.IncludesPlan).HasDefaultValue(true).HasSentinel(true).IsRequired();
        // Asientos y add-ons que cubre el pago de lo vencido.
        builder.OwnsMany(
            intent => intent.Items,
            item =>
            {
                item.ToTable("SubscriptionRenewalIntentItems");
                item.WithOwner().HasForeignKey("SubscriptionRenewalIntentId");
                item.HasKey(i => i.Id);
                item.Property(i => i.Id).ValueGeneratedNever();
                item.Property(i => i.Kind).HasMaxLength(SubscriptionRenewalIntentItem.KindMaxLength).IsRequired();
                item.Property(i => i.TargetId).IsRequired();
                item.Property(i => i.AmountCents).IsRequired();
            }
        );
        builder.Navigation(intent => intent.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(intent => intent.TenantId);
        builder.HasIndex(intent => intent.SaaSPaymentId);
    }
}
