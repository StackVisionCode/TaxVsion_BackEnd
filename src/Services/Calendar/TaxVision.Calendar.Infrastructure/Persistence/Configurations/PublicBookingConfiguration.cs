using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Infrastructure.Persistence.Configurations;

public sealed class PublicBookingConfiguration : IEntityTypeConfiguration<PublicBooking>
{
    // Pendiente de verificar (0) o confirmada (1): las que ocupan el horario.
    private const string Holding = "[Status] IN (0, 1)";

    public void Configure(EntityTypeBuilder<PublicBooking> builder)
    {
        builder.Ignore(b => b.DomainEvents);
        builder.Ignore(b => b.SlotEndUtc);
        builder.Ignore(b => b.IsClosed);

        builder.ToTable("PublicBookings");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.TenantId).IsRequired();
        builder.Property(b => b.BookingPageId).IsRequired();
        builder.Property(b => b.Status).HasConversion<int>().IsRequired();

        builder.Property(b => b.Name).HasMaxLength(ProspectName.MaxLength).IsRequired();
        builder.Property(b => b.Email).HasMaxLength(WorkEmail.MaxLength);
        builder.Property(b => b.RequesterKey).HasColumnType("char(64)");
        builder.Property(b => b.Company).HasMaxLength(CompanyName.MaxLength).IsRequired();
        builder.Property(b => b.OfficeSize).HasConversion<int?>();
        builder.Property(b => b.Phone).HasMaxLength(ProspectPhone.MaxLength);
        builder.Property(b => b.Interest).HasMaxLength(DemoInterest.MaxLength);
        builder.Property(b => b.HeardAboutCode).HasMaxLength(40).IsRequired();
        builder.Property(b => b.HeardAboutDetail).HasMaxLength(HeardAboutAnswer.MaxDetailLength);
        builder.Property(b => b.EntrySource).HasConversion<int>().IsRequired();
        builder.Property(b => b.UtmSource).HasMaxLength(BookingAttribution.MaxUtmLength);
        builder.Property(b => b.UtmMedium).HasMaxLength(BookingAttribution.MaxUtmLength);
        builder.Property(b => b.UtmCampaign).HasMaxLength(BookingAttribution.MaxUtmLength);
        builder.Property(b => b.ReferrerDomain).HasMaxLength(253);
        builder.Property(b => b.Language).HasConversion<int>().IsRequired();
        builder.Property(b => b.VisitorTimeZone).HasMaxLength(64).IsRequired();
        builder.Property(b => b.ConsentTextVersion).HasMaxLength(BookingPage.MaxConsentVersionLength).IsRequired();

        builder.Property(b => b.SlotStartUtc).IsRequired();
        builder.Property(b => b.DurationMinutes).IsRequired();
        builder.Property(b => b.HostUserId).IsRequired();
        builder.Property(b => b.IdempotencyKey).HasMaxLength(PublicBooking.MaxIdempotencyKeyLength).IsRequired();

        // Solo hashes SHA-256 de los secretos (código, manejo de la solicitud, enlace de gestión).
        builder.Property(b => b.RequestHandleHash).HasColumnType("varbinary(32)").IsRequired();
        builder.Property(b => b.CodeHash).HasColumnType("varbinary(32)");
        builder.Property(b => b.ManagementTokenHash).HasColumnType("varbinary(32)");

        builder.Property(b => b.CancelledBy).HasConversion<int?>();
        builder.Property(b => b.CancelReason).HasMaxLength(PublicBooking.MaxReasonLength);
        builder.Property(b => b.RowVersion).IsRowVersion();

        // Reintento del mismo envío: misma solicitud.
        builder
            .HasIndex(b => new
            {
                b.TenantId,
                b.BookingPageId,
                b.IdempotencyKey,
            })
            .IsUnique();
        builder.HasIndex(b => b.RequestHandleHash).IsUnique();
        builder.HasIndex(b => b.ManagementTokenHash).IsUnique().HasFilter("[ManagementTokenHash] IS NOT NULL");

        // Dos visitantes no se quedan con el mismo horario del mismo anfitrión, aunque falle el candado.
        builder
            .HasIndex(b => new
            {
                b.TenantId,
                b.HostUserId,
                b.SlotStartUtc,
            })
            .IsUnique()
            .HasFilter(Holding)
            .HasDatabaseName("UX_PublicBookings_Host_Slot_Holding");

        // Una reserva activa por visitante y página; la pendiente vieja se reemplaza antes de pedir otra.
        builder
            .HasIndex(b => new
            {
                b.TenantId,
                b.BookingPageId,
                b.RequesterKey,
            })
            .IsUnique()
            .HasFilter(Holding)
            .HasDatabaseName("UX_PublicBookings_Requester_Holding");
        builder.HasIndex(b => new { b.Status, b.HoldExpiresAtUtc });
        builder.HasIndex(b => new { b.Status, b.SlotStartUtc });
        builder.HasIndex(b => b.AppointmentId).HasFilter("[AppointmentId] IS NOT NULL");
    }
}
