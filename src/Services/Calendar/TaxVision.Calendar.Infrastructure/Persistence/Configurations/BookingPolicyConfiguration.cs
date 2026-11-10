using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Calendar.Domain.Booking;

namespace TaxVision.Calendar.Infrastructure.Persistence.Configurations;

public sealed class BookingPolicyConfiguration : IEntityTypeConfiguration<BookingPolicy>
{
    public void Configure(EntityTypeBuilder<BookingPolicy> builder)
    {
        builder.ToTable("BookingPolicies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.BookableTypeIdsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();
        // Las oficinas que ya tenían política quedan con aprobación, el valor por defecto acordado.
        builder.Property(p => p.RequiresApproval).HasDefaultValue(true);
        builder.Ignore(p => p.BookableTypeIds);

        builder.HasIndex(p => p.TenantId).IsUnique();
    }
}
