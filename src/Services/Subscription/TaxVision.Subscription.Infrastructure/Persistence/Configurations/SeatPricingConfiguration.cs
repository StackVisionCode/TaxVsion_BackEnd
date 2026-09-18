using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class SeatPricingConfiguration : IEntityTypeConfiguration<SeatPricing>
{
    public void Configure(EntityTypeBuilder<SeatPricing> builder)
    {
        builder.ToTable("SeatPricings");
        builder.HasKey(pricing => pricing.Id);

        builder.Property(pricing => pricing.CreatedAtUtc).IsRequired();
        builder.Property(pricing => pricing.UpdatedAtUtc).IsRequired();
        builder.Property(pricing => pricing.UpdatedBy).IsRequired();

        builder
            .HasMany(pricing => pricing.PriceTiers)
            .WithOne()
            .HasForeignKey(tier => tier.SeatPricingId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(pricing => pricing.PriceTiers).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
