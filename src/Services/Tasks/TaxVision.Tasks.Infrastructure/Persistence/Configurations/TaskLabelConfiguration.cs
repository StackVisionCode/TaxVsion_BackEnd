using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.Domain.Labels;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Infrastructure.Persistence.Configurations;

public sealed class TaskLabelConfiguration : IEntityTypeConfiguration<TaskLabel>
{
    public void Configure(EntityTypeBuilder<TaskLabel> builder)
    {
        builder.ToTable("TaskLabels");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.TenantId).IsRequired();

        builder
            .Property(l => l.Code)
            .HasConversion(code => code.Value, value => TaskLabelCode.Create(value).Value)
            .HasMaxLength(TaskLabelCode.MaxLength)
            .IsRequired();

        builder.Property(l => l.DisplayName).HasMaxLength(TaskLabel.DisplayNameMaxLength).IsRequired();

        builder
            .Property(l => l.Color)
            .HasConversion(color => color.Value, value => LabelColor.Create(value).Value)
            .HasMaxLength(7)
            .IsRequired();

        builder.Property(l => l.MapsToStatus).HasConversion<int>().IsRequired();
        builder.Property(l => l.SortOrder).IsRequired();

        // El código es la clave estable que guarda el front: dos labels con el mismo dentro de un
        // tenant lo volverían ambiguo.
        builder.HasIndex(l => new { l.TenantId, l.Code }).IsUnique().HasDatabaseName("UX_TaskLabels_TenantId_Code");
    }
}
