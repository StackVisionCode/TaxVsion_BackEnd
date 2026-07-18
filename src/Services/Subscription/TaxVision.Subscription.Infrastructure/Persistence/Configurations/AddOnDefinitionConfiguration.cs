using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class AddOnDefinitionConfiguration : IEntityTypeConfiguration<AddOnDefinition>
{
    public void Configure(EntityTypeBuilder<AddOnDefinition> builder)
    {
        builder.ToTable("AddOnDefinitions");
        builder.HasKey(definition => definition.Id);

        builder
            .Property(definition => definition.Code)
            .HasConversion(code => code.Value, value => AddOnCode.Create(value).Value)
            .HasMaxLength(50)
            .IsRequired();
        builder.HasIndex(definition => definition.Code).IsUnique();

        builder.Property(definition => definition.Name).HasMaxLength(200).IsRequired();
        builder.Property(definition => definition.Description).HasMaxLength(2000).IsRequired();
        builder.Property(definition => definition.Category).HasMaxLength(50).IsRequired();
        builder.Property(definition => definition.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(definition => definition.AllowMultipleInstances).IsRequired();

        var billingCyclesConverter = new ValueConverter<List<BillingCycle>, string>(
            cycles => string.Join(',', cycles),
            csv => ParseBillingCycles(csv)
        );
        var billingCyclesComparer = new ValueComparer<List<BillingCycle>>(
            (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
            list => list.Aggregate(0, (hash, cycle) => HashCode.Combine(hash, cycle)),
            list => list.ToList()
        );

        builder
            .Property<List<BillingCycle>>("_supportedBillingCycles")
            .HasColumnName("SupportedBillingCycles")
            .HasConversion(billingCyclesConverter)
            .HasMaxLength(200)
            .Metadata.SetValueComparer(billingCyclesComparer);

        builder
            .HasMany(definition => definition.Features)
            .WithOne()
            .HasForeignKey(feature => feature.AddOnDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(definition => definition.Features).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder
            .HasMany(definition => definition.Entitlements)
            .WithOne()
            .HasForeignKey(entitlement => entitlement.AddOnDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(definition => definition.Entitlements).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder
            .HasMany(definition => definition.PriceTiers)
            .WithOne()
            .HasForeignKey(tier => tier.AddOnDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(definition => definition.PriceTiers).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static List<BillingCycle> ParseBillingCycles(string csv) =>
        string.IsNullOrEmpty(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<BillingCycle>).ToList();
}
