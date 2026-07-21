using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Programs;
using TaxVision.Referrals.Domain.Qualifications;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Referrals;

public sealed class ReferralRewardCaseConfiguration : IEntityTypeConfiguration<ReferralRewardCase>
{
    public void Configure(EntityTypeBuilder<ReferralRewardCase> builder)
    {
        builder.ToTable("ReferralRewardCases", GrowthSchemas.Referrals);
        builder.HasKey(reward => reward.Id);

        builder.Property(reward => reward.TenantId).IsRequired();
        builder.Property(reward => reward.ProgramId).IsRequired();
        builder.Property(reward => reward.AttributionId).IsRequired();
        builder.Property(reward => reward.QualificationId).IsRequired();
        builder.Property(reward => reward.TenantScopeId);
        builder.Property(reward => reward.BeneficiaryType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(reward => reward.BeneficiaryId).IsRequired();
        builder.Property(reward => reward.RewardType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(reward => reward.RewardDefinitionKey).HasMaxLength(100).IsRequired();
        builder.Property(reward => reward.GrantId).IsRequired();
        builder.Property(reward => reward.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(reward => reward.EligibleAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(reward => reward.MaterializedBenefitReference).HasMaxLength(200);
        builder.Property(reward => reward.FailureCode).HasMaxLength(100);
        builder.Property(reward => reward.StateReason).HasMaxLength(500);
        builder.Property(reward => reward.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(reward => reward.PayloadFingerprint).HasColumnType("char(64)").IsFixedLength().IsRequired();
        builder.Property(reward => reward.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(reward => reward.UpdatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(reward => reward.CreatedBy).IsRequired();
        builder.Property(reward => reward.UpdatedBy).IsRequired();
        builder.Property(reward => reward.RowVersion).IsRowVersion();

        builder
            .HasOne<ReferralProgram>()
            .WithMany()
            .HasForeignKey(reward => reward.ProgramId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<ReferralAttribution>()
            .WithMany()
            .HasForeignKey(reward => reward.AttributionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<ReferralQualification>()
            .WithMany()
            .HasForeignKey(reward => reward.QualificationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(reward => reward.GrantId).IsUnique().HasDatabaseName("UX_ReferralRewardCases_GrantId");
        builder
            .HasIndex(reward => new
            {
                reward.QualificationId,
                reward.BeneficiaryType,
                reward.BeneficiaryId,
                reward.RewardType,
            })
            .IsUnique()
            .HasDatabaseName("UX_ReferralRewardCases_Qualification_Beneficiary_Reward");
        builder
            .HasIndex(reward => new { reward.TenantId, reward.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_ReferralRewardCases_TenantId_IdempotencyKey");
    }
}
