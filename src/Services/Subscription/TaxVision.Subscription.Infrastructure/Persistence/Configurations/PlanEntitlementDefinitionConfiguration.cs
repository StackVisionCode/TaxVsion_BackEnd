using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class PlanEntitlementDefinitionConfiguration : IEntityTypeConfiguration<PlanEntitlementDefinition>
{
    public void Configure(EntityTypeBuilder<PlanEntitlementDefinition> builder)
    {
        builder.ToTable("PlanEntitlementDefinitions");
        builder.HasKey(entitlement => entitlement.Id);

        // *** GUARDRAIL persistencia (§49) ***
        // Id se genera en la factory de dominio y la entidad cuelga de
        // SubscriptionPlanVersion._entitlements (HasMany) -> requiere ValueGeneratedNever().
        builder.Property(entitlement => entitlement.Id).ValueGeneratedNever();

        builder.Property(entitlement => entitlement.PlanVersionId).IsRequired();

        builder.Property(entitlement => entitlement.Key)
            .HasConversion(key => key.Value, value => EntitlementKey.Create(value).Value)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(entitlement => entitlement.ValueType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(entitlement => entitlement.DefaultValue).HasMaxLength(200).IsRequired();
        builder.Property(entitlement => entitlement.Description).HasMaxLength(500).IsRequired();

        builder.HasIndex(entitlement => new { entitlement.PlanVersionId, entitlement.Key }).IsUnique();
    }
}
