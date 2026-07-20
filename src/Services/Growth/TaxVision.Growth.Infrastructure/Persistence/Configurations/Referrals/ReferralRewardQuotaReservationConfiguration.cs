using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Growth.Infrastructure.Persistence.Referrals;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Referrals;

public sealed class ReferralRewardQuotaReservationConfiguration
    : IEntityTypeConfiguration<ReferralRewardQuotaReservation>
{
    public void Configure(EntityTypeBuilder<ReferralRewardQuotaReservation> builder)
    {
        builder.ToTable(
            "ReferralRewardQuotaReservations",
            GrowthSchemas.Referrals,
            table =>
                table.HasCheckConstraint(
                    "CK_ReferralRewardQuotaReservations_Year",
                    "[CalendarYear] BETWEEN 2000 AND 9999"
                )
        );
        builder.HasKey(reservation => reservation.Id);

        builder.Property(reservation => reservation.TenantId).IsRequired();
        builder.Property(reservation => reservation.ProgramId).IsRequired();
        builder.Property(reservation => reservation.ReferrerId).IsRequired();
        builder.Property(reservation => reservation.CalendarYear).IsRequired();
        builder.Property(reservation => reservation.QualificationId).IsRequired();
        builder.Property(reservation => reservation.ReservedAtUtc).HasColumnType("datetime2(7)").IsRequired();

        // QualificationId is intentionally a business correlation rather than an FK:
        // the slot must be claimed atomically before the new qualification is inserted.
        builder
            .HasIndex(reservation => reservation.QualificationId)
            .IsUnique()
            .HasDatabaseName("UX_ReferralRewardQuotaReservations_QualificationId");
        builder
            .HasIndex(reservation => new
            {
                reservation.TenantId,
                reservation.ProgramId,
                reservation.ReferrerId,
                reservation.CalendarYear,
            })
            .HasDatabaseName("IX_ReferralRewardQuotaReservations_Quota");
    }
}
