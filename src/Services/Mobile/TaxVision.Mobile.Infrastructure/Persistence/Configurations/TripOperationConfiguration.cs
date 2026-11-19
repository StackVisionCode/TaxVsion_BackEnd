using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Infrastructure.Persistence.Configurations;

public sealed class TripOperationConfiguration : IEntityTypeConfiguration<TripOperation>
{
    public void Configure(EntityTypeBuilder<TripOperation> b)
    {
        b.ToTable("MobileTripStateChanges");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasMaxLength(16);
        b.Property(x => x.PayloadHash).HasMaxLength(64);
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
