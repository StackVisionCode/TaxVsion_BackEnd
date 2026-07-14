using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionPlanVersionConfiguration : IEntityTypeConfiguration<SubscriptionPlanVersion>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlanVersion> builder)
    {
        builder.ToTable("SubscriptionPlanVersions");
        builder.HasKey(version => version.Id);

        // *** GUARDRAIL persistencia (§49) ***
        // Id se genera en la factory de dominio (Guid.NewGuid() vía BaseEntity) y la
        // entidad cuelga de SubscriptionPlan._versions (HasMany). Sin esto, EF marca la
        // entidad como Unchanged/Modified en lugar de Added -> UPDATE de fila inexistente
        // -> DbUpdateConcurrencyException.
        builder.Property(version => version.Id).ValueGeneratedNever();

        builder.Property(version => version.PlanId).IsRequired();
        builder.Property(version => version.VersionNumber).IsRequired();
        builder.Property(version => version.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(version => version.EffectiveFromUtc).IsRequired();
        builder.Property(version => version.TrialDaysDefault).IsRequired();

        var billingCyclesConverter = new ValueConverter<List<BillingCycle>, string>(
            cycles => string.Join(',', cycles),
            csv => ParseBillingCycles(csv));
        var billingCyclesComparer = new ValueComparer<List<BillingCycle>>(
            (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
            list => list.Aggregate(0, (hash, cycle) => HashCode.Combine(hash, cycle)),
            list => list.ToList());

        builder.Property<List<BillingCycle>>("_supportedBillingCycles")
            .HasColumnName("SupportedBillingCycles")
            .HasConversion(billingCyclesConverter)
            .HasMaxLength(200)
            .Metadata.SetValueComparer(billingCyclesComparer);

        builder.HasIndex(version => new { version.PlanId, version.VersionNumber }).IsUnique();
        builder.HasIndex(version => version.PlanId)
            .HasFilter("[Status] = 'Published'")
            .IsUnique()
            .HasDatabaseName("UX_SubscriptionPlanVersions_PlanId_Published");

        builder.HasMany(version => version.Features)
            .WithOne()
            .HasForeignKey(feature => feature.PlanVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(version => version.Features).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(version => version.Entitlements)
            .WithOne()
            .HasForeignKey(entitlement => entitlement.PlanVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(version => version.Entitlements).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(version => version.PriceTiers)
            .WithOne()
            .HasForeignKey(tier => tier.PlanVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(version => version.PriceTiers).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static List<BillingCycle> ParseBillingCycles(string csv) =>
        string.IsNullOrEmpty(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(Enum.Parse<BillingCycle>)
                .ToList();
}
