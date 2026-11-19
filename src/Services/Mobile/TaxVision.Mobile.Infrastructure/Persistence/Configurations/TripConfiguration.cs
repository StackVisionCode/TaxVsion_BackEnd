using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Mobile.Domain.Trips;
using TaxVision.Mobile.Domain.Vehicles;

namespace TaxVision.Mobile.Infrastructure.Persistence.Configurations;

public sealed class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> b)
    {
        b.ToTable(
            "MobileTrips",
            t =>
            {
                t.HasCheckConstraint(
                    "CK_MobileTrips_Distance",
                    "[DistanceMeters] >= 0 AND [ActiveDurationSeconds] >= 0"
                );
                t.HasCheckConstraint("CK_MobileTrips_Revision", "[Revision] >= 1 AND [LastPointSequence] >= 0");
            }
        );
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasAlternateKey(x => new
        {
            x.TenantId,
            x.CustomerId,
            x.Id,
        });
        b.HasIndex(x => new
            {
                x.TenantId,
                x.CustomerId,
                x.ClientTripId,
            })
            .IsUnique();
        b.HasIndex(x => new
        {
            x.TenantId,
            x.CustomerId,
            x.StartedAtUtc,
        });
        b.HasIndex(x => new
        {
            x.TenantId,
            x.CustomerId,
            x.Status,
        });
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Purpose).HasMaxLength(16);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.VehiclePlate).HasMaxLength(32);
        b.Property(x => x.RatePerMile).HasPrecision(10, 4);
        b.Property(x => x.EstimatedAmount).HasPrecision(19, 2);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.HasOne<Vehicle>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.CustomerId,
                x.VehicleId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.CustomerId,
                x.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MileageRate>().WithMany().HasForeignKey(x => x.MileageRateId).OnDelete(DeleteBehavior.Restrict);
    }
}
