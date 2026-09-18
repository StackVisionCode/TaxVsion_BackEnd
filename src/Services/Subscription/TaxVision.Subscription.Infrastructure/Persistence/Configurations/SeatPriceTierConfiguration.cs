using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class SeatPriceTierConfiguration : IEntityTypeConfiguration<SeatPriceTier>
{
    public void Configure(EntityTypeBuilder<SeatPriceTier> builder)
    {
        builder.ToTable("SeatPriceTiers");
        builder.HasKey(tier => tier.Id);

        // *** GUARDRAIL persistencia (§49) ***
        // Id se genera en la factory de dominio y la entidad cuelga de
        // SeatPricing._priceTiers (HasMany) -> requiere ValueGeneratedNever().
        builder.Property(tier => tier.Id).ValueGeneratedNever();

        builder.Property(tier => tier.SeatPricingId).IsRequired();
        builder.Property(tier => tier.SeatType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(tier => tier.BillingCycle).HasConversion<string>().HasMaxLength(20).IsRequired();

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
                tier.SeatPricingId,
                tier.SeatType,
                tier.BillingCycle,
            })
            .IsUnique();
    }
}
