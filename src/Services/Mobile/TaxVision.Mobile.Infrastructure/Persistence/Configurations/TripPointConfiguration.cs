using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Infrastructure.Persistence.Configurations;

public sealed class TripPointConfiguration : IEntityTypeConfiguration<TripPoint>
{
    public void Configure(EntityTypeBuilder<TripPoint> b)
    {
        b.ToTable(
            "MobileTripPoints",
            t =>
            {
                t.HasCheckConstraint(
                    "CK_MobileTripPoints_Location",
                    "[Latitude] >= -90 AND [Latitude] <= 90 AND [Longitude] >= -180 AND [Longitude] <= 180 AND [AccuracyMeters] >= 0 AND [AccuracyMeters] <= 10000"
                );
                t.HasCheckConstraint("CK_MobileTripPoints_Sequence", "[Sequence] >= 0");
            }
        );
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new
            {
                x.TenantId,
                x.CustomerId,
                x.TripId,
                x.Sequence,
            })
            .IsUnique();
        b.HasOne<Trip>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.CustomerId,
                x.TripId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.CustomerId,
                x.Id,
            })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
