using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Modules;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class ModuleConfiguration : IEntityTypeConfiguration<Module>
{
    public void Configure(EntityTypeBuilder<Module> builder)
    {
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => m.Name).IsUnique();
        builder.Property(m => m.Name).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Description).HasMaxLength(500).IsRequired();
        builder.Property(m => m.Url).HasMaxLength(500);
    }
}

public sealed class PlanModuleConfiguration : IEntityTypeConfiguration<PlanModule>
{
    public void Configure(EntityTypeBuilder<PlanModule> builder)
    {
        builder.HasKey(pm => new { pm.PlanId, pm.ModuleId });
        builder.ToTable("PlanModules");

        builder.HasOne(pm => pm.Module)
            .WithMany(m => m.PlanModules)
            .HasForeignKey(pm => pm.ModuleId)
            .OnDelete(DeleteBehavior.Cascade);

        // Plan side is configured in Plan, no WithMany needed here as Plan doesn't have nav collection of PlanModule
        builder.HasIndex(pm => pm.PlanId);
        builder.HasIndex(pm => pm.ModuleId);
    }
}
