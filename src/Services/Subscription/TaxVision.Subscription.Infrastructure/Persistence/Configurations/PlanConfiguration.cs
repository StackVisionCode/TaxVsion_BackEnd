using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => p.Code).IsUnique();
        builder.Property(p => p.Code).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Title).HasMaxLength(150);
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.Currency).HasMaxLength(3);
        builder.Property(p => p.BasePriceMonthly).HasPrecision(19, 4);
        builder.Property(p => p.BasePriceAnnual).HasPrecision(19, 4);
        builder.Property(p => p.PricePerAdditionalSeat).HasPrecision(19, 4);
        builder.Property(p => p.ServiceLevel).IsRequired();

        builder.HasMany(p => p.Features)
            .WithOne()
            .HasForeignKey(f => f.PlanId)
            .OnDelete(DeleteBehavior.Cascade);

        // PlanModules relationship is configured in ModuleConfiguration
    }
}

public sealed class PlanFeatureConfiguration : IEntityTypeConfiguration<PlanFeature>
{
    public void Configure(EntityTypeBuilder<PlanFeature> builder)
    {
        builder.HasKey(f => f.Id);
        builder.HasIndex(f => new { f.PlanId, f.FeatureCode }).IsUnique();
        builder.Property(f => f.FeatureCode).HasMaxLength(100).IsRequired();
    }
}
