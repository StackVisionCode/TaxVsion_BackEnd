using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Infrastructure.Persistence.Configurations;

/// <summary>
/// Los VOs de un solo valor van por <c>HasConversion</c> a una columna de la raíz; los multi-campo
/// (<see cref="DueDate"/>, <see cref="TaskReference"/>) van <c>OwnsOne</c> con nombres de columna
/// planos. El reparto decide qué índices se pueden declarar: un owned type es otro entity type y
/// <c>HasIndex</c> no los cruza, así que los índices sobre sus columnas se crean con SQL directo en
/// la migración.
/// </summary>
public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("Tasks");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TenantId).IsRequired();
        builder.Property(t => t.CreatedByUserId).IsRequired();
        builder.Property(t => t.AssigneeUserId);
        builder.Property(t => t.Status).HasConversion<int>().IsRequired();
        builder.Property(t => t.Priority).HasConversion<int>().IsRequired();
        builder.Property(t => t.CreatedAtUtc).IsRequired();
        builder.Property(t => t.StartedAtUtc);
        builder.Property(t => t.CompletedAtUtc);

        builder.Property(t => t.ParentTaskId);
        builder.Property(t => t.Depth).IsRequired();
        builder.Property(t => t.OpenSubtaskCount).IsRequired();
        builder.Property(t => t.OpenBlockerCount).IsRequired();
        builder.Property(t => t.SeriesId);
        builder.Property(t => t.TemplateId);
        builder.Property(t => t.OverdueNotifiedAtUtc);
        builder.Property(t => t.OccurrenceNumber);

        builder.Property(t => t.ActualHours).HasColumnType("decimal(8,2)").IsRequired();

        // Derivada de OpenBlockerCount: si EF la mapeara habría dos fuentes de verdad.
        builder.Ignore(t => t.IsBlocked);

        builder.HasMany(t => t.Timers).WithOne().HasForeignKey(t => t.TaskId).OnDelete(DeleteBehavior.Cascade);

        // Timers se lee del backing field. Sin esto EF descubriría la propiedad de solo lectura por
        // convención y rompería las migraciones.
        builder.Metadata.FindNavigation(nameof(TaskItem.Timers))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(t => t.Attachments).WithOne().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(TaskItem.Attachments))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        // El handler de desbloqueo y el usuario que completa pueden tocar la misma fila a la vez.
        builder.Property(t => t.RowVersion).IsRowVersion();

        ConfigureSingleValueObjects(builder);
        ConfigureOwnedValueObjects(builder);
        ConfigureClientRequest(builder);
        ConfigureIndexes(builder);
    }

    private static void ConfigureSingleValueObjects(EntityTypeBuilder<TaskItem> builder)
    {
        builder
            .Property(t => t.Description)
            .HasConversion(description => description!.Value, value => TaskDescription.Create(value).Value)
            .HasMaxLength(TaskDescription.MaxLength);

        builder
            .Property(t => t.Estimated)
            .HasConversion(estimated => estimated!.Value, value => EstimatedHours.Create(value).Value)
            .HasColumnType($"decimal(6,{EstimatedHours.Scale})");
    }

    private static void ConfigureOwnedValueObjects(EntityTypeBuilder<TaskItem> builder)
    {
        // Nombres planos (DueAtUtc, no Due_DueAtUtc): los nombran las consultas y los índices por SQL.
        builder.OwnsOne(
            t => t.Due,
            due =>
            {
                due.Property(d => d.DueAtUtc).HasColumnName("DueAtUtc");
                due.Property(d => d.TimeZoneId).HasColumnName("DueTimeZoneId").HasMaxLength(64);
                due.Property(d => d.IsStatutory).HasColumnName("DueIsStatutory");
            }
        );

        // El título va OwnsOne y no HasConversion aunque sea de un solo valor: sobre una propiedad
        // convertida EF no traduce `Contains`, y la búsqueda por texto lo necesita. Misma columna y
        // mismo tipo; no hay índice sobre el título, así que no se pierde nada por cruzar entity types.
        builder.OwnsOne(
            t => t.Title,
            title =>
            {
                title.Property(x => x.Value).HasColumnName("Title").HasMaxLength(TaskTitle.MaxLength).IsRequired();
            }
        );
        builder.Navigation(t => t.Title).IsRequired();

        builder.OwnsOne(
            t => t.Reference,
            reference =>
            {
                reference.Property(r => r.CustomerId).HasColumnName("CustomerId");
                reference.Property(r => r.TaxYear).HasColumnName("TaxYear");
            }
        );
        builder.Navigation(t => t.Reference).IsRequired();
    }

    /// <summary>
    /// Las cuatro columnas de la petición al cliente. <c>ClientRequestedByUserId</c> se persiste
    /// porque <c>task.client_responded.v1</c> lo publica un consumer de CloudStorage semanas después,
    /// cuando el llamado a <c>MoveToWaitingOnClient</c> ya no existe en ningún lado.
    /// </summary>
    private static void ConfigureClientRequest(EntityTypeBuilder<TaskItem> builder)
    {
        builder
            .Property(t => t.ExpectedItems)
            .HasConversion(items => items!.Value, value => ClientRequestNote.Create(value).Value)
            .HasColumnName("ExpectedItems")
            .HasMaxLength(ClientRequestNote.MaxLength);

        builder.Property(t => t.ClientDueAtUtc);
        builder.Property(t => t.ClientRequestedByUserId);
        builder.Property(t => t.ClientRequestedAtUtc);
    }

    private static void ConfigureIndexes(EntityTypeBuilder<TaskItem> builder)
    {
        builder.HasIndex(t => new { t.TenantId, t.ParentTaskId }).HasDatabaseName("IX_Tasks_TenantId_ParentTaskId");

        builder.HasIndex(t => new { t.TenantId, t.SeriesId }).HasDatabaseName("IX_Tasks_TenantId_SeriesId");

        // Sólo (TenantId, TemplateId): CustomerId vive dentro del owned Reference y HasIndex no cruza
        // entity types. Basta — una plantilla rinde decenas de filas, y sobre ellas se filtra el
        // cliente y el año.
        builder.HasIndex(t => new { t.TenantId, t.TemplateId }).HasDatabaseName("IX_Tasks_TenantId_TemplateId");

        // Filtrado, para la pantalla de seguimiento. Se puede declarar acá porque ClientDueAtUtc vive
        // en la raíz, no dentro de un owned type.
        builder
            .HasIndex(t => new
            {
                t.TenantId,
                t.Status,
                t.ClientDueAtUtc,
            })
            .HasFilter($"[Status] = {(int)TaskItemStatus.WaitingOnClient}")
            .HasDatabaseName("IX_Tasks_TenantId_Status_ClientDueAtUtc_WaitingOnClient");

        // Los otros tres —(TenantId, AssigneeUserId, Status, DueAtUtc), (TenantId, CustomerId,
        // TaxYear) y (Status, DueAtUtc)— tocan columnas de un owned type y se crean con SQL directo
        // en la migración AddTasks. No aparecen en el ModelSnapshot.
    }
}
