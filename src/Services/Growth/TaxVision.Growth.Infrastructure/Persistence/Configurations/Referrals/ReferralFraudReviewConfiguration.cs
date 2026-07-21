using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Fraud;
using TaxVision.Referrals.Domain.Programs;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Referrals;

public sealed class ReferralFraudReviewConfiguration : IEntityTypeConfiguration<ReferralFraudReview>
{
    public void Configure(EntityTypeBuilder<ReferralFraudReview> builder)
    {
        builder.ToTable(
            "ReferralFraudReviews",
            GrowthSchemas.Referrals,
            table =>
                table.HasCheckConstraint(
                    "CK_ReferralFraudReviews_Target",
                    "[AttributionId] IS NOT NULL OR [RewardCaseId] IS NOT NULL"
                )
        );
        builder.HasKey(review => review.Id);

        builder.Property(review => review.TenantId).IsRequired();
        builder.Property(review => review.ProgramId).IsRequired();
        builder.Property(review => review.TenantScopeId);
        builder.Property(review => review.AttributionId);
        builder.Property(review => review.RewardCaseId);
        builder.Property(review => review.SignalCode).HasMaxLength(100).IsRequired();
        builder.Property(review => review.EvidenceReference).HasMaxLength(500).IsRequired();
        builder.Property(review => review.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(review => review.ResolutionReason).HasMaxLength(1000);
        builder.Property(review => review.ResolvedBy);
        builder.Property(review => review.ResolvedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(review => review.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(review => review.PayloadFingerprint).HasColumnType("char(64)").IsFixedLength().IsRequired();
        builder.Property(review => review.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(review => review.UpdatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(review => review.CreatedBy).IsRequired();
        builder.Property(review => review.UpdatedBy).IsRequired();
        builder.Property(review => review.RowVersion).IsRowVersion();

        builder
            .HasOne<ReferralProgram>()
            .WithMany()
            .HasForeignKey(review => review.ProgramId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<ReferralAttribution>()
            .WithMany()
            .HasForeignKey(review => review.AttributionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<ReferralRewardCase>()
            .WithMany()
            .HasForeignKey(review => review.RewardCaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(review => new { review.Status, review.CreatedAtUtc })
            .HasDatabaseName("IX_ReferralFraudReviews_Status_CreatedAtUtc");
        builder
            .HasIndex(review => new { review.TenantId, review.Status })
            .HasDatabaseName("IX_ReferralFraudReviews_TenantId_Status");
        builder
            .HasIndex(review => new { review.TenantId, review.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_ReferralFraudReviews_TenantId_IdempotencyKey");
    }
}
