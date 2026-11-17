using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionDiscountConfiguration : IEntityTypeConfiguration<SubscriptionDiscount>
{
    public void Configure(EntityTypeBuilder<SubscriptionDiscount> builder)
    {
        builder.ToTable("SubscriptionDiscounts");
        builder.HasKey(discount => discount.Id);

        // Cuelga de TenantSubscription._discounts (HasMany) y su Id nace en el dominio -> ValueGeneratedNever().
        builder.Property(discount => discount.Id).ValueGeneratedNever();

        builder.Property(discount => discount.TenantSubscriptionId).IsRequired();
        builder.Property(discount => discount.TenantId).IsRequired();
        builder.Property(discount => discount.Source).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(discount => discount.Duration).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(discount => discount.AppliesTo).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(discount => discount.Currency).HasMaxLength(3);
        builder.Property(discount => discount.PlanCode).HasMaxLength(50);
        builder.Property(discount => discount.Reason).HasMaxLength(500);
        builder.Property(discount => discount.CodeDisplay).HasMaxLength(SubscriptionDiscountTerms.CodeDisplayMaxLength);
        builder.Property(discount => discount.EndedReason).HasMaxLength(500);
        builder.Property(discount => discount.StartsAtUtc).IsRequired();
        builder.Property(discount => discount.CreatedAtUtc).IsRequired();

        builder
            .HasIndex(discount => new { discount.TenantSubscriptionId, discount.EndedAtUtc })
            .HasDatabaseName("IX_SubscriptionDiscounts_TenantSubscriptionId_EndedAtUtc");

        // El job de aviso de fin busca los descuentos con fecha de fin que siguen vigentes.
        builder
            .HasIndex(discount => discount.EndsAtUtc)
            .HasFilter("[EndedAtUtc] IS NULL AND [EndsAtUtc] IS NOT NULL")
            .HasDatabaseName("IX_SubscriptionDiscounts_EndsAtUtc_Open");
    }
}
