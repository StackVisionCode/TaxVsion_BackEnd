using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class AddOnFeatureConfiguration : IEntityTypeConfiguration<AddOnFeature>
{
    public void Configure(EntityTypeBuilder<AddOnFeature> builder)
    {
        builder.ToTable("AddOnFeatures");
        builder.HasKey(feature => feature.Id);

        // *** GUARDRAIL persistencia (§49) ***
        // Id se genera en la factory de dominio y la entidad cuelga de
        // AddOnDefinition._features (HasMany) -> requiere ValueGeneratedNever().
        builder.Property(feature => feature.Id).ValueGeneratedNever();

        builder.Property(feature => feature.AddOnDefinitionId).IsRequired();

        builder.Property(feature => feature.FeatureKey)
            .HasConversion(key => key.Value, value => EntitlementKey.Create(value).Value)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(feature => feature.Enabled).IsRequired();

        builder.HasIndex(feature => new { feature.AddOnDefinitionId, feature.FeatureKey }).IsUnique();
    }
}
