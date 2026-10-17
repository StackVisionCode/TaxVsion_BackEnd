using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Catalog;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class ModuleConfiguration : IEntityTypeConfiguration<Module>
{
    public void Configure(EntityTypeBuilder<Module> builder)
    {
        builder.ToTable("Modules");
        builder.HasKey(module => module.Id);
        builder.Property(module => module.Id).ValueGeneratedNever();
        builder.Property(module => module.Code).HasMaxLength(50).IsRequired();
        builder.HasIndex(module => module.Code).IsUnique();
        builder.Property(module => module.Name).HasMaxLength(120).IsRequired();
        builder.Property(module => module.Description).HasMaxLength(500).IsRequired();
        builder.Property(module => module.Category).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(module => module.AddonPriceMonthlyUsd).HasColumnType("decimal(10,2)");
        builder.Property(module => module.AddonPriceYearlyUsd).HasColumnType("decimal(10,2)");
        builder.Ignore(module => module.IsSellableAsAddon);
        builder.Ignore(module => module.AddOnCode);
    }
}
