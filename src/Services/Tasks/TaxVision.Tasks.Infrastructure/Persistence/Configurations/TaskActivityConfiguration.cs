using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.Domain.Activity;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Infrastructure.Persistence.Configurations;

public sealed class TaskActivityConfiguration : IEntityTypeConfiguration<TaskActivity>
{
    public void Configure(EntityTypeBuilder<TaskActivity> builder)
    {
        builder.ToTable("TaskActivities");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(a => a.DataJson).HasMaxLength(TaskActivity.DataMaxLength);
        builder.Property(a => a.OccurredAtUtc).IsRequired();

        // Borrar la tarea borra su historial.
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex(a => new { a.TaskId, a.OccurredAtUtc })
            .HasDatabaseName("IX_TaskActivities_TaskId_OccurredAtUtc");
    }
}
