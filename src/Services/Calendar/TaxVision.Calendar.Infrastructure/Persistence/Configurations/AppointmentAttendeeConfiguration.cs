using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Infrastructure.Persistence.Configurations;

public sealed class AppointmentAttendeeConfiguration : IEntityTypeConfiguration<AppointmentAttendee>
{
    public void Configure(EntityTypeBuilder<AppointmentAttendee> builder)
    {
        builder.ToTable("AppointmentAttendees");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.AppointmentId).IsRequired();
        builder.Property(a => a.Kind).HasConversion<int>().IsRequired();
        builder.Property(a => a.UserId);
        builder.Property(a => a.CustomerId);
        builder.Property(a => a.IsRequired).IsRequired();
        builder.Property(a => a.Response).HasConversion<int>().IsRequired();
        builder.Property(a => a.RespondedAtUtc);

        // Nombre y correo son snapshot: dos valores, asi que OwnsOne y no HasConversion.
        builder.OwnsOne(
            a => a.Snapshot,
            snapshot =>
            {
                snapshot
                    .Property(s => s.DisplayName)
                    .HasColumnName("DisplayName")
                    .HasMaxLength(AttendeeSnapshot.MaxNameLength)
                    .IsRequired();

                snapshot.Property(s => s.Email).HasColumnName("Email").HasMaxLength(AttendeeSnapshot.MaxEmailLength);
            }
        );

        builder.Navigation(a => a.Snapshot).IsRequired();

        builder.HasIndex(a => a.AppointmentId);

        // El detector de conflictos entra por el usuario: sin este indice recorre los asistentes de
        // todas las citas del tenant.
        builder.HasIndex(a => new { a.UserId, a.AppointmentId });
    }
}
