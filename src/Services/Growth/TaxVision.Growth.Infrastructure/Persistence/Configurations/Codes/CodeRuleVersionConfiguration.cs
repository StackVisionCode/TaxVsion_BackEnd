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
        // El Id lo pone el dominio: así una versión nueva sobre un código ya guardado se inserta (no UPDATE).
        builder.Property(rule => rule.Id).ValueGeneratedNever();

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
                    percentage => percentage.Property(value => value.Value).HasColumnName("BenefitBasisPoints")
                );
                benefit.OwnsOne(value => value.FixedAmount, amount => amount.ConfigureMoney("BenefitFixed"));
            }
        );
        builder.Navigation(rule => rule.Benefit).IsRequired();
        builder.OwnsOne(rule => rule.MinimumPurchase, minimum => minimum.ConfigureMoney("MinimumPurchase"));
        // Las reglas de antes quedan en "solo el primer cobro".
        builder.OwnsOne(
            rule => rule.Duration,
            duration =>
            {
                duration
                    .Property(value => value.Kind)
                    .HasColumnName("DurationKind")
                    .HasConversion<string>()
                    .HasMaxLength(20)
                    .HasDefaultValue(CodeDurationKind.Once)
                    .IsRequired();
                duration.Property(value => value.Months).HasColumnName("DurationMonths");
            }
        );
        builder.Navigation(rule => rule.Duration).IsRequired();

        builder
            .HasIndex(rule => new { rule.CodeDefinitionId, rule.Version })
            .IsUnique()
            .HasDatabaseName("UX_CodeRules_CodeDefinitionId_Version");
    }
}
