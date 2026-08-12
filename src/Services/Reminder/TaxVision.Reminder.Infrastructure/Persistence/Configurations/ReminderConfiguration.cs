using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Infrastructure.Persistence.Configurations;

public sealed class ReminderConfiguration : IEntityTypeConfiguration<ReminderAggregate>
{
    public void Configure(EntityTypeBuilder<ReminderAggregate> builder)
    {
        builder.ToTable("Reminders");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.UserId).IsRequired();
        builder.Property(r => r.Status).HasConversion<int>().IsRequired();
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.FiredAtUtc);
        builder.Property(r => r.ResolvedAtUtc);
        builder.Property(r => r.CancellationReason).HasMaxLength(100);
        builder.Property(r => r.SnoozeCount).IsRequired();

        // Concurrencia optimista: Quartz puede disparar mientras el usuario pospone.
        builder.Property(r => r.RowVersion).IsRowVersion();

        // VOs de un solo valor -> columna en la RAÍZ vía HasConversion, no OwnsOne. No es
        // cosmético: sólo así `RequestKey` puede formar parte de un índice compuesto junto con
        // `TenantId` (un owned type vive en otro entity type y EF no cruza los dos en HasIndex).
        // Mismo patrón que `CodeReservation.ReservationIdempotencyKey` en Growth.
        builder
            .Property(r => r.RequestKey)
            .HasConversion(key => key.Value, value => RequestKey.Create(value).Value)
            .HasMaxLength(RequestKey.MaxLength)
            .IsRequired();

        builder
            .Property(r => r.TimeZone)
            .HasConversion(tz => tz.Value, value => ReminderTimeZone.Create(value).Value)
            .HasMaxLength(64)
            .IsRequired();

        builder.OwnsOne(
            r => r.Subject,
            subject =>
            {
                subject
                    .Property(s => s.Title)
                    .HasColumnName("Title")
                    .HasMaxLength(ReminderSubject.MaxTitleLength)
                    .IsRequired();
                subject.Property(s => s.Body).HasColumnName("Body").HasMaxLength(ReminderSubject.MaxBodyLength);
            }
        );
        builder.Navigation(r => r.Subject).IsRequired();

        builder.OwnsOne(
            r => r.Target,
            target =>
            {
                target.Property(t => t.Category).HasColumnName("Category").HasConversion<int>().IsRequired();
                target.Property(t => t.TargetId).HasColumnName("TargetId");

                // Índice 3 de 4 — resuelve target_moved/target_closed. Queda declarado DENTRO del
                // owned type, sin TenantId, por la misma limitación de EF que se documenta abajo.
                // El filtro global fail-closed acota por tenant antes de tocar este índice.
                target.HasIndex(t => new { t.Category, t.TargetId }).HasDatabaseName("IX_Reminders_Category_TargetId");
            }
        );
        builder.Navigation(r => r.Target).IsRequired();

        builder.OwnsOne(
            r => r.Schedule,
            schedule =>
            {
                schedule.Property(s => s.FireAtUtc).HasColumnName("FireAtUtc").IsRequired();
                schedule.Property(s => s.AnchorAtUtc).HasColumnName("AnchorAtUtc");
                schedule.Property(s => s.LeadMinutes).HasColumnName("LeadMinutes");
            }
        );
        builder.Navigation(r => r.Schedule).IsRequired();

        // Índice 1 de 4 — soporte físico de ADR-R-07. Sin esto la idempotencia es una intención,
        // no una garantía: dos redeliveries concurrentes pasarían los dos el chequeo de lectura.
        builder
            .HasIndex(r => new { r.TenantId, r.RequestKey })
            .IsUnique()
            .HasDatabaseName("UX_Reminders_TenantId_RequestKey");

        // Índices 2 y 4 NO están aquí: los dos necesitan FireAtUtc, que vive en el owned type
        // Schedule. Medido en EF Core 10 con `HasIndex(r => new { r.TenantId, ..., r.Schedule.FireAtUtc })`:
        //   "The expression '...' is not a valid member access expression. The expression should
        //    represent a simple property or field access"
        // EF no cruza entity types en HasIndex aunque compartan tabla (table splitting). Se crean
        // con SQL directo en la migración AddReminders, sobre las columnas reales — ver el
        // comentario ahí. No son opcionales: el índice (Status, FireAtUtc) es lo único que evita
        // un scan completo cross-tenant en el job de reconciliación de la Fase 5.
    }
}
