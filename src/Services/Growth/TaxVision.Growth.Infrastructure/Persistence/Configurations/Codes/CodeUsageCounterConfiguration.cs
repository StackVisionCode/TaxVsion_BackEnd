using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.Usage;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Codes;

public sealed class CodeUsageCounterConfiguration : IEntityTypeConfiguration<CodeUsageCounter>
{
    public void Configure(EntityTypeBuilder<CodeUsageCounter> builder)
    {
        builder.ToTable(
            "CodeUsageCounters",
            GrowthSchemas.Codes,
            table =>
            {
                table.HasCheckConstraint("CK_CodeUsageCounters_Limit", "[MaxRedemptions] > 0");
                table.HasCheckConstraint(
                    "CK_CodeUsageCounters_Counts",
                    "[ActiveReservations] >= 0 AND [CommittedRedemptions] >= 0 "
                        + "AND [ActiveReservations] + [CommittedRedemptions] <= [MaxRedemptions]"
                );
            }
        );
        builder.HasKey(counter => counter.Id);

        builder.Property(counter => counter.TenantId).IsRequired();
        builder.Property(counter => counter.Dimension).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder
            .Property(counter => counter.ScopeKey)
            .HasConversion(key => key.Value, value => CodeUsageScopeKey.Create(value).Value)
            .HasMaxLength(250)
            .IsRequired();
        builder.Property(counter => counter.MaxRedemptions).IsRequired();
        builder.Property(counter => counter.ActiveReservations).IsRequired();
        builder.Property(counter => counter.CommittedRedemptions).IsRequired();
        builder.Property(counter => counter.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(counter => counter.UpdatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(counter => counter.RowVersion).IsRowVersion();

        builder
            .HasOne<CodeDefinition>()
            .WithMany()
            .HasForeignKey(counter => counter.CodeDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(counter => new
            {
                counter.TenantId,
                counter.CodeDefinitionId,
                counter.Dimension,
                counter.ScopeKey,
            })
            .IsUnique()
            .HasDatabaseName("UX_CodeUsageCounters_Tenant_Code_Dimension_Scope");
    }
}
