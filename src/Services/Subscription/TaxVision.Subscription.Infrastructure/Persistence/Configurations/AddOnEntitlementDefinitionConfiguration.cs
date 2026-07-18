using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class AddOnEntitlementDefinitionConfiguration : IEntityTypeConfiguration<AddOnEntitlementDefinition>
{
    public void Configure(EntityTypeBuilder<AddOnEntitlementDefinition> builder)
    {
        builder.ToTable("AddOnEntitlementDefinitions");
        builder.HasKey(entitlement => entitlement.Id);

        // *** GUARDRAIL persistencia (§49) ***
        // Id se genera en la factory de dominio y la entidad cuelga de
        // AddOnDefinition._entitlements (HasMany) -> requiere ValueGeneratedNever().
        builder.Property(entitlement => entitlement.Id).ValueGeneratedNever();

        builder.Property(entitlement => entitlement.AddOnDefinitionId).IsRequired();

        builder
            .Property(entitlement => entitlement.Key)
            .HasConversion(key => key.Value, value => EntitlementKey.Create(value).Value)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(entitlement => entitlement.ValueType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(entitlement => entitlement.Value).HasMaxLength(200).IsRequired();
        builder
            .Property(entitlement => entitlement.MergeStrategy)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(entitlement => new { entitlement.AddOnDefinitionId, entitlement.Key }).IsUnique();
    }
}
