using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Growth.Infrastructure.Persistence.Referrals;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Referrals;

public sealed class ReferralRewardQuotaCounterConfiguration
    : IEntityTypeConfiguration<ReferralRewardQuotaCounter>
{
    public void Configure(EntityTypeBuilder<ReferralRewardQuotaCounter> builder)
    {
        builder.ToTable(
            "ReferralRewardQuotaCounters",
            GrowthSchemas.Referrals,
            table =>
            {
                table.HasCheckConstraint(
                    "CK_ReferralRewardQuotaCounters_Year",
                    "[CalendarYear] BETWEEN 2000 AND 9999"
                );
                table.HasCheckConstraint(
                    "CK_ReferralRewardQuotaCounters_Count",
                    "[Maximum] > 0 AND [ReservedCount] >= 0 AND [ReservedCount] <= [Maximum]"
                );
            }
        );
        builder.HasKey(counter => counter.Id);

        builder.Property(counter => counter.TenantId).IsRequired();
        builder.Property(counter => counter.ProgramId).IsRequired();
        builder.Property(counter => counter.ReferrerId).IsRequired();
        builder.Property(counter => counter.CalendarYear).IsRequired();
        builder.Property(counter => counter.Maximum).IsRequired();
        builder.Property(counter => counter.ReservedCount).IsRequired();
        builder.Property(counter => counter.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(counter => counter.UpdatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(counter => counter.RowVersion).IsRowVersion();

        builder
            .HasIndex(counter => new
            {
                counter.TenantId,
                counter.ProgramId,
                counter.ReferrerId,
                counter.CalendarYear,
            })
            .IsUnique()
            .HasDatabaseName("UX_ReferralRewardQuotaCounters_Owner_Program_Referrer_Year");
    }
}
