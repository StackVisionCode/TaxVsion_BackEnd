using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Calendar.Domain.Booking;

namespace TaxVision.Calendar.Infrastructure.Persistence.Configurations;

public sealed class BookingRequestConfiguration : IEntityTypeConfiguration<BookingRequest>
{
    public void Configure(EntityTypeBuilder<BookingRequest> builder)
    {
        builder.ToTable("BookingRequests");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.TenantId).IsRequired();
        builder.Property(b => b.CustomerId).IsRequired();
        builder.Property(b => b.IdempotencyKey).HasMaxLength(BookingRequest.MaxKeyLength).IsRequired();
        builder.Property(b => b.AppointmentId).IsRequired();
        builder.Property(b => b.CreatedAtUtc).IsRequired();

        // Una reserva por clave y cliente: el reintento choca aquí y devuelve la misma cita.
        builder
            .HasIndex(b => new
            {
                b.TenantId,
                b.CustomerId,
                b.IdempotencyKey,
            })
            .IsUnique();
    }
}
