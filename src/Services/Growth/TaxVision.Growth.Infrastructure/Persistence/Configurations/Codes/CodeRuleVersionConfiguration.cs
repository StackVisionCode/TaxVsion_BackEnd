using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Codes.Domain.Definitions;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Codes;

public sealed class CodeRuleVersionConfiguration : IEntityTypeConfiguration<CodeRuleVersion>
{
    public void Configure(EntityTypeBuilder<CodeRuleVersion> builder)
    {
        builder.ToTable("CodeRules", GrowthSchemas.Codes);
        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.TenantId).IsRequired();
        builder.Property(rule => rule.Version).IsRequired();
        builder.Property(rule => rule.PublishedAtUtc).HasColumnType("datetime2(7)").IsRequired();

        builder.OwnsOne(
            rule => rule.Benefit,
            benefit =>
            {
                benefit.Property(value => value.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
                benefit.Property(value => value.GrantKey).HasMaxLength(200);
                benefit.OwnsOne(
                    value => value.Percentage,
                    percentage =>
                        percentage.Property(value => value.Value).HasColumnName("BenefitBasisPoints")
                );
                benefit.OwnsOne(
                    value => value.FixedAmount,
                    amount => amount.ConfigureMoney("BenefitFixed")
                );
            }
        );
        builder.Navigation(rule => rule.Benefit).IsRequired();
        builder.OwnsOne(
            rule => rule.MinimumPurchase,
            minimum => minimum.ConfigureMoney("MinimumPurchase")
        );

        builder
            .HasIndex(rule => new { rule.CodeDefinitionId, rule.Version })
            .IsUnique()
            .HasDatabaseName("UX_CodeRules_CodeDefinitionId_Version");
    }
}
