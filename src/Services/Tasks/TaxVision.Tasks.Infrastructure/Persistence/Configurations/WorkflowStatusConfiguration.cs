using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Infrastructure.Persistence.Configurations;

/// <summary>Antes <c>TaskLabels</c>: la misma fila evoluciona a estado de la oficina.</summary>
public sealed class WorkflowStatusConfiguration : IEntityTypeConfiguration<WorkflowStatus>
{
    public void Configure(EntityTypeBuilder<WorkflowStatus> builder)
    {
        builder.ToTable("TaskWorkflowStatuses");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.TenantId).IsRequired();

        builder
            .Property(s => s.Code)
            .HasConversion(code => code.Value, value => TaskLabelCode.Create(value).Value)
            .HasMaxLength(TaskLabelCode.MaxLength)
            .IsRequired();

        builder.Property(s => s.DisplayName).HasMaxLength(WorkflowStatus.DisplayNameMaxLength).IsRequired();

        builder
            .Property(s => s.Color)
            .HasConversion(color => color.Value, value => LabelColor.Create(value).Value)
            .HasMaxLength(7)
            .IsRequired();

        builder.Property(s => s.Category).HasConversion<int>().IsRequired();
        builder.Property(s => s.SortOrder).IsRequired();
        builder.Property(s => s.IsDefault).IsRequired();
        builder.Property(s => s.IsArchived).IsRequired();
        builder.Property(s => s.ArchivedAtUtc);
        builder.Property(s => s.Description).HasMaxLength(WorkflowStatus.DescriptionMaxLength);
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.Property(s => s.CreatedByUserId);
        builder.Property(s => s.CreatedAtUtc);
        builder.Property(s => s.UpdatedByUserId);
        builder.Property(s => s.UpdatedAtUtc);

        // El código es la clave estable que guardan el front y las preferencias.
        builder
            .HasIndex(s => new { s.TenantId, s.Code })
            .IsUnique()
            .HasDatabaseName("UX_TaskWorkflowStatuses_TenantId_Code");

        // Un solo estado por defecto activo por categoría.
        builder
            .HasIndex(s => new { s.TenantId, s.Category })
            .IsUnique()
            .HasFilter("[IsDefault] = 1 AND [IsArchived] = 0")
            .HasDatabaseName("UX_TaskWorkflowStatuses_TenantId_Category_Default");

        // El nombre es único entre los activos de la oficina (la intercalación no distingue mayúsculas).
        builder
            .HasIndex(s => new { s.TenantId, s.DisplayName })
            .IsUnique()
            .HasFilter("[IsArchived] = 0")
            .HasDatabaseName("UX_TaskWorkflowStatuses_TenantId_DisplayName_Active");
    }
}
