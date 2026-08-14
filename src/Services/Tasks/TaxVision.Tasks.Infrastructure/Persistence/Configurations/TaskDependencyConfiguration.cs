using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.Domain.Dependencies;

namespace TaxVision.Tasks.Infrastructure.Persistence.Configurations;

public sealed class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> builder)
    {
        builder.ToTable("TaskDependencies");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.TenantId).IsRequired();
        builder.Property(d => d.TaskId).IsRequired();
        builder.Property(d => d.DependsOnTaskId).IsRequired();
        builder.Property(d => d.Type).HasConversion<int>().IsRequired();
        builder.Property(d => d.CreatedByUserId).IsRequired();
        builder.Property(d => d.CreatedAtUtc).IsRequired();

        // D3 se cumple en la BD, no en el handler: dos requests simultáneos pasan la validación y
        // sólo el índice frena al segundo.
        builder
            .HasIndex(d => new
            {
                d.TenantId,
                d.TaskId,
                d.DependsOnTaskId,
            })
            .IsUnique();

        // Cada nivel de la consulta recursiva salta por DependsOnTaskId; sin esto es un scan por nivel.
        builder.HasIndex(d => new { d.TenantId, d.DependsOnTaskId });
    }
}
