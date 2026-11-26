using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Infrastructure.Persistence.Configurations;

/// <summary>Catálogo global de tasas del IRS, sembrado desde <see cref="IrsStandardMileageRates"/>.</summary>
public sealed class StandardMileageRateConfiguration : IEntityTypeConfiguration<StandardMileageRate>
{
    public void Configure(EntityTypeBuilder<StandardMileageRate> b)
    {
        b.ToTable(
            "MobileStandardMileageRates",
            t =>
                t.HasCheckConstraint(
                    "CK_MobileStandardMileageRates_Rate",
                    "[RatePerMile] >= 0 AND [RatePerMile] <= 1000"
                )
        );
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Purpose).HasMaxLength(16);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Source).HasMaxLength(64);
        b.Property(x => x.RatePerMile).HasPrecision(10, 4);
        b.HasIndex(x => new { x.Purpose, x.EffectiveFrom }).IsUnique();
        b.HasData(IrsStandardMileageRates.All.Select(p => StandardMileageRate.From(p, IrsStandardMileageRates.Source)));
    }
}

public sealed class StaffMileageAccessLogConfiguration : IEntityTypeConfiguration<StaffMileageAccessLog>
{
    public void Configure(EntityTypeBuilder<StaffMileageAccessLog> b)
    {
        b.ToTable("MobileStaffAccessLogs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Action).HasMaxLength(32);
        b.HasIndex(x => new
        {
            x.TenantId,
            x.CustomerId,
            x.OccurredAtUtc,
        });
    }
}
