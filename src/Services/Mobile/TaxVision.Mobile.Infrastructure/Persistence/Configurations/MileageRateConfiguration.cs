using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Infrastructure.Persistence.Configurations;

public sealed class MileageRateConfiguration : IEntityTypeConfiguration<MileageRate>
{
    public void Configure(EntityTypeBuilder<MileageRate> b)
    {
        b.ToTable(
            "MobileMileageRates",
            t => t.HasCheckConstraint("CK_MobileMileageRates_Rate", "[RatePerMile] >= 0 AND [RatePerMile] <= 1000")
        );
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Purpose).HasMaxLength(16);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.RatePerMile).HasPrecision(10, 4);
        b.HasIndex(x => new
            {
                x.TenantId,
                x.Purpose,
                x.EffectiveFrom,
            })
            .IsUnique();
    }
}
