using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Referrals;

public sealed class ReferrerBenefitConfiguration : IEntityTypeConfiguration<ReferrerBenefit>
{
    public void Configure(EntityTypeBuilder<ReferrerBenefit> builder)
    {
        builder.ToTable(
            "ReferrerBenefits",
            GrowthSchemas.Referrals,
            table =>
            {
                table.HasCheckConstraint(
                    "CK_ReferrerBenefits_Amounts",
                    "[TotalBenefitWalletCents] > 0 AND [AppliedBasisPoints] BETWEEN 1 AND 10000"
                );
            }
        );
        builder.HasKey(benefit => benefit.Id);

        builder.Property(benefit => benefit.TenantId).IsRequired();
        builder.Property(benefit => benefit.RewardCaseId).IsRequired();
        builder.Property(benefit => benefit.AttributionId).IsRequired();
        builder.Property(benefit => benefit.AppliedBasisPoints).IsRequired();
        builder.Property(benefit => benefit.TotalBenefitWalletCents).IsRequired();
        builder.Property(benefit => benefit.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(benefit => benefit.SystemActorId).IsRequired();
        builder.Property(benefit => benefit.CreditedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(benefit => benefit.ReversedAtUtc).HasColumnType("datetime2(7)");

        builder
            .HasOne<ReferralRewardCase>()
            .WithMany()
            .HasForeignKey(benefit => benefit.RewardCaseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<ReferralAttribution>()
            .WithMany()
            .HasForeignKey(benefit => benefit.AttributionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Un beneficio por caso de premio: hace idempotente el registro ante reintentos del worker.
        builder
            .HasIndex(benefit => benefit.RewardCaseId)
            .IsUnique()
            .HasDatabaseName("UX_ReferrerBenefits_RewardCaseId");
        builder
            .HasIndex(benefit => new { benefit.TenantId, benefit.CreditedAtUtc })
            .HasDatabaseName("IX_ReferrerBenefits_TenantId_CreditedAtUtc");
    }
}
