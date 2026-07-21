using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Referrals;

public sealed class ReferralAttributionConfiguration : IEntityTypeConfiguration<ReferralAttribution>
{
    public void Configure(EntityTypeBuilder<ReferralAttribution> builder)
    {
        builder.ToTable(
            "ReferralAttributions",
            GrowthSchemas.Referrals,
            table =>
                table.HasCheckConstraint(
                    "CK_ReferralAttributions_NoSelfReferral",
                    "[ReferrerType] <> [RefereeType] OR [ReferrerId] <> [RefereeId]"
                )
        );
        builder.HasKey(attribution => attribution.Id);

        builder.Property(attribution => attribution.TenantId).IsRequired();
        builder.Property(attribution => attribution.ProgramId).IsRequired();
        builder.Property(attribution => attribution.ReferralCodeId).IsRequired();
        builder.Property(attribution => attribution.TenantScopeId);
        builder.Property(attribution => attribution.ReferrerType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(attribution => attribution.ReferrerId).IsRequired();
        builder.Property(attribution => attribution.RefereeType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(attribution => attribution.RefereeId).IsRequired();
        builder.Property(attribution => attribution.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(attribution => attribution.StatusBeforeReview).HasConversion<string>().HasMaxLength(20);
        builder.Property(attribution => attribution.AttributedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(attribution => attribution.ExpiresAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(attribution => attribution.QualifiedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(attribution => attribution.RejectedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(attribution => attribution.RejectionReason).HasMaxLength(500);
        builder.Property(attribution => attribution.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder
            .Property(attribution => attribution.PayloadFingerprint)
            .HasColumnType("char(64)")
            .IsFixedLength()
            .IsRequired();
        builder.Property(attribution => attribution.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(attribution => attribution.UpdatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(attribution => attribution.CreatedBy).IsRequired();
        builder.Property(attribution => attribution.UpdatedBy).IsRequired();
        builder.Property(attribution => attribution.RowVersion).IsRowVersion();

        builder
            .HasOne<ReferralProgram>()
            .WithMany()
            .HasForeignKey(attribution => attribution.ProgramId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<ReferralCode>()
            .WithMany()
            .HasForeignKey(attribution => attribution.ReferralCodeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(attribution => new
            {
                attribution.ProgramId,
                attribution.RefereeType,
                attribution.RefereeId,
            })
            .HasFilter("[Status] IN (N'Pending', N'Active', N'Qualified', N'UnderReview')")
            .IsUnique()
            .HasDatabaseName("UX_ReferralAttributions_ActiveReferee");
        builder
            .HasIndex(attribution => new { attribution.TenantId, attribution.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_ReferralAttributions_TenantId_IdempotencyKey");
    }
}
