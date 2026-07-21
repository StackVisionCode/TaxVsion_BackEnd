using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Referrals;

public sealed class ReferralRewardAttemptConfiguration : IEntityTypeConfiguration<ReferralRewardAttempt>
{
    public void Configure(EntityTypeBuilder<ReferralRewardAttempt> builder)
    {
        builder.ToTable("ReferralRewardAttempts", GrowthSchemas.Referrals);
        builder.HasKey(attempt => attempt.Id);

        builder.Property(attempt => attempt.TenantId).IsRequired();
        builder.Property(attempt => attempt.RewardCaseId).IsRequired();
        builder.Property(attempt => attempt.TenantScopeId);
        builder.Property(attempt => attempt.Operation).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(attempt => attempt.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(attempt => attempt.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(attempt => attempt.PayloadFingerprint).HasColumnType("char(64)").IsFixedLength().IsRequired();
        builder.Property(attempt => attempt.ExternalReference).HasMaxLength(200);
        builder.Property(attempt => attempt.FailureCode).HasMaxLength(100);
        builder.Property(attempt => attempt.FailureReason).HasMaxLength(500);
        builder.Property(attempt => attempt.CompletionIdempotencyKey).HasMaxLength(200);
        builder.Property(attempt => attempt.CompletionPayloadFingerprint).HasColumnType("char(64)").IsFixedLength();
        builder.Property(attempt => attempt.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(attempt => attempt.CompletedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(attempt => attempt.CreatedBy).IsRequired();
        builder.Property(attempt => attempt.UpdatedBy).IsRequired();
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        builder
            .HasOne<ReferralRewardCase>()
            .WithMany()
            .HasForeignKey(attempt => attempt.RewardCaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(attempt => new { attempt.RewardCaseId, attempt.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_ReferralRewardAttempts_RewardCase_IdempotencyKey");
        builder
            .HasIndex(attempt => new { attempt.RewardCaseId, attempt.CompletionIdempotencyKey })
            .HasFilter("[CompletionIdempotencyKey] IS NOT NULL")
            .IsUnique()
            .HasDatabaseName("UX_ReferralRewardAttempts_RewardCase_CompletionKey");
    }
}
