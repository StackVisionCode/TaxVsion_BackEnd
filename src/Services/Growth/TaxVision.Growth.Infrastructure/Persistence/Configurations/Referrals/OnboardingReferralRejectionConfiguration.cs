using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Referrals.Domain.Attributions;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Referrals;

public sealed class OnboardingReferralRejectionConfiguration : IEntityTypeConfiguration<OnboardingReferralRejection>
{
    public void Configure(EntityTypeBuilder<OnboardingReferralRejection> builder)
    {
        builder.ToTable("OnboardingReferralRejections", GrowthSchemas.Referrals);
        builder.HasKey(rejection => rejection.Id);

        builder.Property(rejection => rejection.TenantId).IsRequired();
        builder.Property(rejection => rejection.OnboardingId).IsRequired();
        builder
            .Property(rejection => rejection.ReasonCode)
            .HasMaxLength(OnboardingReferralRejection.ReasonCodeMaxLength)
            .IsRequired();
        builder.Property(rejection => rejection.RejectedAtUtc).HasColumnType("datetime2(7)").IsRequired();

        // Una sola anotación por alta (el consumer de la declaración puede llegar dos veces).
        builder
            .HasIndex(rejection => rejection.OnboardingId)
            .IsUnique()
            .HasDatabaseName("UX_OnboardingReferralRejections_OnboardingId");
    }
}
