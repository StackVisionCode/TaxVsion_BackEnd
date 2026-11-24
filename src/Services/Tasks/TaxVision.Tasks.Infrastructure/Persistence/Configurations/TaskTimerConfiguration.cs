using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Infrastructure.Persistence.Configurations;

/// <summary>
/// Sin columna de tenant: el timer se alcanza siempre desde su tarea, que sí la lleva. Una segunda
/// copia del tenant sería una segunda fuente de verdad que puede quedar desalineada.
/// </summary>
public sealed class TaskTimerConfiguration : IEntityTypeConfiguration<TaskTimer>
{
    public void Configure(EntityTypeBuilder<TaskTimer> builder)
    {
        builder.ToTable("TaskTimers");
        builder.HasKey(t => t.Id);

        // El Guid lo genera el dominio: sin esto EF intenta un UPDATE en vez de un INSERT.
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.TaskId).IsRequired();
        builder.Property(t => t.UserId).IsRequired();
        builder.Property(t => t.StartedAtUtc).IsRequired();
        builder.Property(t => t.StoppedAtUtc);
        builder.Property(t => t.IsBillable).IsRequired();
        builder.Property(t => t.IsManual).IsRequired();
        builder.Property(t => t.Note).HasMaxLength(TaskTimer.NoteMaxLength);
        builder.Property(t => t.UpdatedAtUtc);
        builder.Property(t => t.UpdatedByUserId);

        // Derivadas de las dos marcas de tiempo.
        builder.Ignore(t => t.IsRunning);
        builder.Ignore(t => t.Hours);

        // El reporte de horas filtra por persona y rango.
        builder.HasIndex(t => new { t.UserId, t.StartedAtUtc }).HasDatabaseName("IX_TaskTimers_UserId_StartedAtUtc");

        // El de la clave foránea se declara: el único filtrado no sirve para cargar todos los tramos de una tarea.
        builder.HasIndex(t => t.TaskId).HasDatabaseName("IX_TaskTimers_TaskId");

        // Un temporizador en marcha por persona y tarea: la regla del dominio, también en la base.
        builder
            .HasIndex(t => new { t.TaskId, t.UserId })
            .IsUnique()
            .HasFilter("[StoppedAtUtc] IS NULL")
            .HasDatabaseName("UX_TaskTimers_Running");
    }
}
