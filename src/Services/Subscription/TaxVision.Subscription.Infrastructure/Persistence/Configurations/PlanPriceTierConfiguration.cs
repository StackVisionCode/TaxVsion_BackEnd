using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class PlanPriceTierConfiguration : IEntityTypeConfiguration<PlanPriceTier>
{
    public void Configure(EntityTypeBuilder<PlanPriceTier> builder)
    {
        builder.ToTable("PlanPriceTiers");
        builder.HasKey(tier => tier.Id);

        // *** GUARDRAIL persistencia (§49) ***
        // Id se genera en la factory de dominio y la entidad cuelga de
        // SubscriptionPlanVersion._priceTiers (HasMany) -> requiere ValueGeneratedNever().
        builder.Property(tier => tier.Id).ValueGeneratedNever();

        builder.Property(tier => tier.PlanVersionId).IsRequired();
        builder.Property(tier => tier.BillingCycle).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(tier => tier.MinQuantity).IsRequired();
        builder.Property(tier => tier.MaxQuantity);

        builder.OwnsOne(
            tier => tier.UnitAmount,
            money =>
            {
                money.Property(m => m.Amount).HasColumnName("UnitAmount").HasPrecision(18, 4).IsRequired();
                money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
            }
        );

        builder
            .HasIndex(tier => new
            {
                tier.PlanVersionId,
                tier.BillingCycle,
                tier.MinQuantity,
            })
            .IsUnique();
    }
}
