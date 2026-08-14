using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Infrastructure.Persistence.Configurations;

public sealed class AppointmentExceptionConfiguration : IEntityTypeConfiguration<AppointmentException>
{
    public void Configure(EntityTypeBuilder<AppointmentException> builder)
    {
        builder.ToTable("AppointmentExceptions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.AppointmentId).IsRequired();
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.OriginalStartUtc).IsRequired();
        builder.Property(e => e.Kind).HasConversion<int>().IsRequired();
        builder.Property(e => e.NewStartUtc);
        builder.Property(e => e.NewEndUtc);
        builder.Property(e => e.NewTitle).HasMaxLength(200);
        builder.Property(e => e.NewLocation).HasMaxLength(300);
        builder.Property(e => e.ModifiedByUserId).IsRequired();
        builder.Property(e => e.ModifiedAtUtc).IsRequired();

        // Una ocurrencia no puede tener dos excepciones. El agregado ya lo comprueba, pero dos
        // peticiones concurrentes pasan las dos comprobaciones y solo el indice las separa.
        builder
            .HasIndex(e => new { e.AppointmentId, e.OriginalStartUtc })
            .IsUnique()
            .HasDatabaseName("UX_AppointmentExceptions_AppointmentId_OriginalStartUtc");
    }
}
