using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.AddOns;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class AddOnPriceTierConfiguration : IEntityTypeConfiguration<AddOnPriceTier>
{
    public void Configure(EntityTypeBuilder<AddOnPriceTier> builder)
    {
        builder.ToTable("AddOnPriceTiers");
        builder.HasKey(tier => tier.Id);

        // *** GUARDRAIL persistencia (§49) ***
        // Id se genera en la factory de dominio y la entidad cuelga de
        // AddOnDefinition._priceTiers (HasMany) -> requiere ValueGeneratedNever().
        builder.Property(tier => tier.Id).ValueGeneratedNever();

        builder.Property(tier => tier.AddOnDefinitionId).IsRequired();
        builder.Property(tier => tier.BillingCycle).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(tier => tier.MinQuantity).IsRequired();
        builder.Property(tier => tier.MaxQuantity);

        builder.OwnsOne(tier => tier.UnitAmount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("UnitAmount").HasPrecision(18, 4).IsRequired();
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
        });

        builder.HasIndex(tier => new { tier.AddOnDefinitionId, tier.BillingCycle, tier.MinQuantity }).IsUnique();
    }
}
