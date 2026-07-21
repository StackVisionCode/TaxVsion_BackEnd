using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Programs;
using TaxVision.Referrals.Domain.Qualifications;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Referrals;

public sealed class ReferralQualificationConfiguration : IEntityTypeConfiguration<ReferralQualification>
{
    public void Configure(EntityTypeBuilder<ReferralQualification> builder)
    {
        builder.ToTable(
            "ReferralQualifications",
            GrowthSchemas.Referrals,
            table => table.HasCheckConstraint("CK_ReferralQualifications_PaymentAmount", "[PaymentAmountCents] > 0")
        );
        builder.HasKey(qualification => qualification.Id);

        builder.Property(qualification => qualification.TenantId).IsRequired();
        builder.Property(qualification => qualification.ProgramId).IsRequired();
        builder.Property(qualification => qualification.AttributionId).IsRequired();
        builder.Property(qualification => qualification.TenantScopeId);
        builder.Property(qualification => qualification.QualifyingEventId).IsRequired();
        builder.Property(qualification => qualification.PaymentId).IsRequired();
        builder
            .Property(qualification => qualification.PaymentSource)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(qualification => qualification.PaymentAmountCents).IsRequired();
        builder.Property(qualification => qualification.PaymentCurrency).HasColumnType("char(3)").IsRequired();
        builder.Property(qualification => qualification.IsFirstSuccessfulPayment).IsRequired();
        builder.Property(qualification => qualification.Decision).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(qualification => qualification.RejectionReasonCode).HasMaxLength(100);
        builder
            .Property(qualification => qualification.PaymentSucceededAtUtc)
            .HasColumnType("datetime2(7)")
            .IsRequired();
        builder.Property(qualification => qualification.RewardEligibleAtUtc).HasColumnType("datetime2(7)");
        builder.Property(qualification => qualification.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder
            .Property(qualification => qualification.PayloadFingerprint)
            .HasColumnType("char(64)")
            .IsFixedLength()
            .IsRequired();
        builder.Property(qualification => qualification.EvaluatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(qualification => qualification.EvaluatedBy).IsRequired();

        builder
            .HasOne<ReferralProgram>()
            .WithMany()
            .HasForeignKey(qualification => qualification.ProgramId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<ReferralAttribution>()
            .WithMany()
            .HasForeignKey(qualification => qualification.AttributionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(qualification => new { qualification.AttributionId, qualification.QualifyingEventId })
            .IsUnique()
            .HasDatabaseName("UX_ReferralQualifications_Attribution_Event");
        builder
            .HasIndex(qualification => new { qualification.TenantId, qualification.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_ReferralQualifications_TenantId_IdempotencyKey");
    }
}
