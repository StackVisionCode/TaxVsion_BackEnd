using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.Domain.Templates;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Infrastructure.Persistence.Configurations;

public sealed class TaskTemplateConfiguration : IEntityTypeConfiguration<TaskTemplate>
{
    public void Configure(EntityTypeBuilder<TaskTemplate> builder)
    {
        builder.ToTable("TaskTemplates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TenantId).IsRequired();
        builder.Property(t => t.CreatedByUserId).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(2000);
        builder.Property(t => t.IsActive).IsRequired();
        builder.Property(t => t.CreatedAtUtc).IsRequired();
        builder.Property(t => t.UpdatedAtUtc);
        builder.Property(t => t.RowVersion).IsRowVersion();

        // El listado que ve el preparador al aplicar una plantilla filtra por activas.
        builder.Property(t => t.RecurrenceMode).HasConversion<int>().IsRequired();

        builder.OwnsOne(
            t => t.Recurrence,
            rule =>
            {
                rule.Property(r => r.Value).HasColumnName("RecurrenceRule").HasMaxLength(RecurrenceRule.MaxLength);
                rule.Property(r => r.TimeZoneId).HasColumnName("RecurrenceTimeZoneId").HasMaxLength(64);
            }
        );

        builder.HasIndex(t => new { t.TenantId, t.IsActive }).HasDatabaseName("IX_TaskTemplates_TenantId_IsActive");

        builder.HasMany(t => t.Steps).WithOne().HasForeignKey(s => s.TaskTemplateId).OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(TaskTemplate.Steps))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(t => t.Attachments).WithOne().HasForeignKey(a => a.TemplateId).OnDelete(DeleteBehavior.Cascade);

        builder
            .Metadata.FindNavigation(nameof(TaskTemplate.Attachments))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class TaskTemplateAttachmentConfiguration : IEntityTypeConfiguration<TaskTemplateAttachment>
{
    public void Configure(EntityTypeBuilder<TaskTemplateAttachment> builder)
    {
        builder.ToTable("TaskTemplateAttachments");
        builder.HasKey(a => a.Id);

        // El Guid lo genera el dominio: sin esto EF intenta un UPDATE en vez de un INSERT.
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.TemplateId).IsRequired();
        builder.Property(a => a.FileId).IsRequired();
        builder.Property(a => a.DisplayName).HasMaxLength(260).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(160);
        builder.Property(a => a.SizeBytes).IsRequired();
        builder.Property(a => a.StepOrder);

        // El mismo archivo una sola vez por guion: dos referencias al mismo PDF darían dos adjuntos
        // idénticos en cada instancia.
        builder
            .HasIndex(a => new { a.TemplateId, a.FileId })
            .IsUnique()
            .HasDatabaseName("UX_TaskTemplateAttachments_TemplateId_FileId");
    }
}

public sealed class TaskTemplateStepConfiguration : IEntityTypeConfiguration<TaskTemplateStep>
{
    public void Configure(EntityTypeBuilder<TaskTemplateStep> builder)
    {
        builder.ToTable("TaskTemplateSteps");
        builder.HasKey(s => s.Id);

        // Los pasos se reemplazan en bloque, así que EF los inserta con el Id que ya trae el dominio.
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.TaskTemplateId).IsRequired();
        builder.Property(s => s.Order).IsRequired();
        builder.Property(s => s.Priority).HasConversion<int>().IsRequired();
        builder.Property(s => s.DueOffsetDays).IsRequired();
        builder.Property(s => s.IsStatutory).IsRequired();
        builder.Property(s => s.DependsOnStepOrder);
        builder.Property(s => s.ParentStepOrder);
        builder.Property(s => s.SuggestedRoleName).HasMaxLength(100);

        builder.OwnsOne(
            s => s.Title,
            title => title.Property(t => t.Value).HasColumnName("Title").HasMaxLength(TaskTitle.MaxLength).IsRequired()
        );

        builder.OwnsOne(
            s => s.Description,
            description =>
                description.Property(d => d.Value).HasColumnName("Description").HasMaxLength(TaskDescription.MaxLength)
        );

        builder.OwnsOne(
            s => s.Estimated,
            estimated => estimated.Property(e => e.Value).HasColumnName("EstimatedHours").HasPrecision(6, 2)
        );

        // Dos pasos con el mismo orden dentro de una plantilla es la invariante que el dominio ya
        // valida; el índice la sostiene si alguien escribe por fuera.
        builder
            .HasIndex(s => new { s.TaskTemplateId, s.Order })
            .IsUnique()
            .HasDatabaseName("UX_TaskTemplateSteps_TemplateId_Order");
    }
}
