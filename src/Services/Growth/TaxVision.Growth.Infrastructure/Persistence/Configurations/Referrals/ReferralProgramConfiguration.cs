using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Referrals;

public sealed class ReferralProgramConfiguration : IEntityTypeConfiguration<ReferralProgram>
{
    public void Configure(EntityTypeBuilder<ReferralProgram> builder)
    {
        builder.ToTable(
            "ReferralPrograms",
            GrowthSchemas.Referrals,
            table =>
            {
                table.HasCheckConstraint(
                    "CK_ReferralPrograms_TenantScope",
                    "([ScopeType] = N'Platform' AND [TenantScopeId] IS NULL) OR "
                        + "([ScopeType] = N'Tenant' AND [TenantScopeId] IS NOT NULL)"
                );
                table.HasCheckConstraint(
                    "CK_ReferralPrograms_Period",
                    "[EndsAtUtc] IS NULL OR [EndsAtUtc] > [StartsAtUtc]"
                );
                table.HasCheckConstraint(
                    "CK_ReferralPrograms_FlowScope",
                    "([FlowType] = N'TenantToTenant' AND [ScopeType] = N'Platform') OR "
                        + "([FlowType] = N'TaxpayerToTaxpayer' AND [ScopeType] = N'Tenant')"
                );
                table.HasCheckConstraint(
                    "CK_ReferralPrograms_PolicyLimits",
                    "[AttributionWindowDays] > 0 AND [MinimumPaymentAmountCents] >= 0 "
                        + "AND [WaitingPeriodDays] >= 0 "
                        + "AND [MaximumRewardsPerReferrerPerCalendarYear] > 0"
                );
            }
        );
        builder.HasKey(program => program.Id);

        builder.Property(program => program.TenantId).IsRequired();
        builder.Property(program => program.ProgramCode).HasMaxLength(50).IsRequired();
        builder.Property(program => program.Name).HasMaxLength(200).IsRequired();
        builder.Property(program => program.ScopeType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(program => program.TenantScopeId);
        builder.Property(program => program.FlowType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(program => program.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(program => program.PolicyVersion).IsRequired();
        builder.Property(program => program.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(program => program.PayloadFingerprint).HasColumnType("char(64)").IsFixedLength().IsRequired();
        builder.Property(program => program.StartsAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(program => program.EndsAtUtc).HasColumnType("datetime2(7)");
        builder.Property(program => program.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(program => program.UpdatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(program => program.CreatedBy).IsRequired();
        builder.Property(program => program.UpdatedBy).IsRequired();
        builder.Property(program => program.RowVersion).IsRowVersion();

        builder.OwnsOne(
            program => program.Policy,
            policy =>
            {
                policy
                    .Property(value => value.AttributionWindowDays)
                    .HasColumnName("AttributionWindowDays")
                    .IsRequired();
                policy
                    .Property(value => value.PaymentSource)
                    .HasColumnName("QualifyingPaymentSource")
                    .HasConversion<string>()
                    .HasMaxLength(30)
                    .IsRequired();
                policy
                    .Property(value => value.QualifyingEvent)
                    .HasColumnName("QualifyingEventRule")
                    .HasConversion<string>()
                    .HasMaxLength(40)
                    .IsRequired();
                policy
                    .Property(value => value.MinimumPaymentAmountCents)
                    .HasColumnName("MinimumPaymentAmountCents")
                    .IsRequired();
                policy
                    .Property(value => value.MinimumPaymentCurrency)
                    .HasColumnName("MinimumPaymentCurrency")
                    .HasColumnType("char(3)");
                policy.Property(value => value.WaitingPeriodDays).HasColumnName("WaitingPeriodDays").IsRequired();
                policy
                    .Property(value => value.MaximumRewardsPerReferrerPerCalendarYear)
                    .HasColumnName("MaximumRewardsPerReferrerPerCalendarYear")
                    .IsRequired();
                policy
                    .Property(value => value.RewardType)
                    .HasColumnName("RewardType")
                    .HasConversion<string>()
                    .HasMaxLength(40)
                    .IsRequired();
                policy
                    .Property(value => value.RewardDefinitionKey)
                    .HasColumnName("RewardDefinitionKey")
                    .HasMaxLength(100)
                    .IsRequired();
                policy
                    .Property(value => value.RefereeBenefitType)
                    .HasColumnName("RefereeBenefitType")
                    .HasConversion<string>()
                    .HasMaxLength(20);
                policy
                    .Property(value => value.RefereeBenefitPercentageBasisPoints)
                    .HasColumnName("RefereeBenefitPercentageBasisPoints");
                policy
                    .Property(value => value.RefereeBenefitFixedAmountCents)
                    .HasColumnName("RefereeBenefitFixedAmountCents");
                policy
                    .Property(value => value.RefereeBenefitCurrency)
                    .HasColumnName("RefereeBenefitCurrency")
                    .HasColumnType("char(3)");
                policy
                    .Property(value => value.RefereeBenefitExpirationDays)
                    .HasColumnName("RefereeBenefitExpirationDays")
                    .IsRequired();
            }
        );
        builder.Navigation(program => program.Policy).IsRequired();

        builder
            .HasIndex(program => new
            {
                program.ScopeType,
                program.TenantScopeId,
                program.ProgramCode,
            })
            .IsUnique()
            .HasDatabaseName("UX_ReferralPrograms_Scope_ProgramCode");
        builder
            .HasIndex(program => new { program.TenantId, program.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_ReferralPrograms_TenantId_IdempotencyKey");
    }
}
