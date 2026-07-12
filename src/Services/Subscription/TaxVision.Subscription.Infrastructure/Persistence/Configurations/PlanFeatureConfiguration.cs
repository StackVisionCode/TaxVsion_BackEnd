using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class PlanFeatureConfiguration : IEntityTypeConfiguration<PlanFeature>
{
    public void Configure(EntityTypeBuilder<PlanFeature> builder)
    {
        builder.ToTable("PlanFeatures");
        builder.HasKey(feature => feature.Id);

        // *** GUARDRAIL persistencia (§49) ***
        // Id se genera en la factory de dominio y la entidad cuelga de
        // SubscriptionPlanVersion._features (HasMany) -> requiere ValueGeneratedNever().
        builder.Property(feature => feature.Id).ValueGeneratedNever();

        builder.Property(feature => feature.PlanVersionId).IsRequired();

        builder.Property(feature => feature.FeatureKey)
            .HasConversion(key => key.Value, value => EntitlementKey.Create(value).Value)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(feature => feature.DefaultEnabled).IsRequired();
        builder.Property(feature => feature.Description).HasMaxLength(500).IsRequired();

        builder.HasIndex(feature => new { feature.PlanVersionId, feature.FeatureKey }).IsUnique();
    }
}
