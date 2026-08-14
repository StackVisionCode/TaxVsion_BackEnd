using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Infrastructure.Persistence.Configurations;

/// <summary>
/// Los VOs de un solo valor van por <c>HasConversion</c> a una columna de la raíz; los multi-campo
/// van <c>OwnsOne</c> con nombres de columna planos. El reparto decide qué índices se pueden
/// declarar: un owned type es otro entity type y <c>HasIndex</c> no los cruza, así que los índices
/// que mezclan sus columnas con las de la raíz se crean con SQL en la migración.
/// </summary>
public sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.Status).HasConversion<int>().IsRequired();
        builder.Property(a => a.AppointmentTypeId).IsRequired();
        builder.Property(a => a.OrganizerUserId).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(4000);
        builder.Property(a => a.CustomerId);
        builder.Property(a => a.TaxYear);
        builder.Property(a => a.IsVirtual).IsRequired();
        builder.Property(a => a.MeetingId);
        builder.Property(a => a.MeetingShortCode).HasMaxLength(64);
        builder.Property(a => a.CancellationReason).HasMaxLength(500);
        builder.Property(a => a.SplitFromSeriesId);
        builder.Property(a => a.CreatedAtUtc).IsRequired();
        builder.Property(a => a.CancelledAtUtc);
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder
            .Property(a => a.Title)
            .HasConversion(title => title.Value, value => AppointmentTitle.Create(value).Value)
            .HasMaxLength(AppointmentTitle.MaxLength)
            .IsRequired();

        builder
            .Property(a => a.Location)
            .HasConversion(location => location!.Value, value => Location.Create(value).Value)
            .HasMaxLength(Location.MaxLength);

        // Null en esta columna es exactamente «cita puntual», y es lo que filtra los dos índices de
        // rango: por eso el VO va a una columna simple y no a un owned type.
        builder
            .Property(a => a.Recurrence)
            .HasColumnName("RecurrenceRule")
            .HasConversion(rule => rule!.Value, value => RecurrenceRule.Create(value).Value)
            .HasMaxLength(RecurrenceRule.MaxLength);

        ConfigureTiming(builder);

        builder.HasIndex(a => new
        {
            a.TenantId,
            a.OrganizerUserId,
            a.Status,
        });
        builder.HasIndex(a => new
        {
            a.TenantId,
            a.CustomerId,
            a.TaxYear,
        });

        // Los dos índices de rango llevan columnas del owned type y del filtro de la raíz a la vez,
        // así que no se pueden declarar acá: viven en la migración, en SQL, y se verifican en
        // sys.indexes — el .cs no prueba que existan.

        builder
            .HasMany(a => a.Attendees)
            .WithOne()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(a => a.Exceptions)
            .WithOne()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(a => a.Attendees).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(a => a.Exceptions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    /// <summary>
    /// Las tres formas de <see cref="EventTiming"/> comparten tabla y cada una llena sus columnas.
    ///
    /// <para>
    /// ⚠️ <c>StartUtc</c> es NULL en toda serie recurrente, y es la regla, no un bug: un recurrente es
    /// hora de pared mas zona, y guardarlo en UTC lo corre una hora al cambiar el horario. Quien vea
    /// esta columna vacia y la «arregle» reintroduce ese bug entero.
    /// </para>
    /// </summary>
    private static void ConfigureTiming(EntityTypeBuilder<Appointment> builder)
    {
        builder.OwnsOne(
            a => a.Timing,
            timing =>
            {
                timing.Property(t => t.Kind).HasColumnName("TimingKind").HasConversion<int>().IsRequired();

                timing
                    .Property(t => t.TimeZone)
                    .HasColumnName("TimeZoneId")
                    .HasConversion(zone => zone.Id, value => CalendarTimeZone.Create(value).Value)
                    .HasMaxLength(64)
                    .IsRequired();

                timing.Property(t => t.StartUtc).HasColumnName("StartUtc");
                timing.Property(t => t.EndUtc).HasColumnName("EndUtc");
                timing.Property(t => t.StartDate).HasColumnName("StartDate");
                timing.Property(t => t.EndDate).HasColumnName("EndDate");
                timing.Property(t => t.LocalStartTime).HasColumnName("LocalStartTime");
                timing.Property(t => t.SeriesStartDate).HasColumnName("SeriesStartDate");
                timing.Property(t => t.Duration).HasColumnName("Duration");
            }
        );

        builder.Navigation(a => a.Timing).IsRequired();
    }
}
