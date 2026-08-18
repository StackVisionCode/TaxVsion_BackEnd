using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Infrastructure.Persistence.Configurations;

public sealed class TaskSeriesConfiguration : IEntityTypeConfiguration<TaskSeries>
{
    public void Configure(EntityTypeBuilder<TaskSeries> builder)
    {
        builder.ToTable("TaskSeries");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.CreatedByUserId).IsRequired();
        builder.Property(s => s.Mode).HasConversion<int>().IsRequired();
        builder.Property(s => s.Status).HasConversion<int>().IsRequired();
        builder.Property(s => s.AnchorUtc).IsRequired();
        builder.Property(s => s.OpenInstanceId);
        builder.Property(s => s.GeneratedCount).IsRequired();
        builder.Property(s => s.SkippedOccurrences).IsRequired();
        builder.Property(s => s.EndsAtUtc);
        builder.Property(s => s.MaxOccurrences);
        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.RowVersion).IsRowVersion();

        ConfigureOwnedValueObjects(builder);

        // El barrido de materialización pregunta por las activas de un tenant; sin este índice
        // recorre la tabla entera en cada pasada.
        builder.HasIndex(s => new { s.TenantId, s.Status }).HasDatabaseName("IX_TaskSeries_TenantId_Status");
    }

    private static void ConfigureOwnedValueObjects(EntityTypeBuilder<TaskSeries> builder)
    {
        builder.OwnsOne(
            s => s.Rule,
            rule =>
            {
                rule.Property(r => r.Value)
                    .HasColumnName("RecurrenceRule")
                    .HasMaxLength(RecurrenceRule.MaxLength)
                    .IsRequired();
                rule.Property(r => r.TimeZoneId).HasColumnName("RecurrenceTimeZoneId").HasMaxLength(64).IsRequired();
            }
        );
        builder.Navigation(s => s.Rule).IsRequired();

        builder.OwnsOne(
            s => s.Blueprint,
            blueprint =>
            {
                blueprint
                    .Property(b => b.Title)
                    .HasConversion(title => title.Value, value => TaskTitle.Create(value).Value)
                    .HasColumnName("Title")
                    .HasMaxLength(TaskTitle.MaxLength)
                    .IsRequired();

                blueprint
                    .Property(b => b.Description)
                    .HasConversion(description => description!.Value, value => TaskDescription.Create(value).Value)
                    .HasColumnName("Description")
                    .HasMaxLength(TaskDescription.MaxLength);

                blueprint.Property(b => b.Priority).HasConversion<int>().HasColumnName("Priority").IsRequired();

                blueprint
                    .Property(b => b.Estimated)
                    .HasConversion(estimated => estimated!.Value, value => EstimatedHours.Create(value).Value)
                    .HasColumnName("EstimatedHours")
                    .HasColumnType($"decimal(6,{EstimatedHours.Scale})");

                blueprint.Property(b => b.AssigneeUserId).HasColumnName("AssigneeUserId").IsRequired();
                blueprint.Property(b => b.IsStatutory).HasColumnName("IsStatutory").IsRequired();

                blueprint.OwnsOne(
                    b => b.Reference,
                    reference =>
                    {
                        reference.Property(r => r.CustomerId).HasColumnName("CustomerId");
                        reference.Property(r => r.TaxYear).HasColumnName("TaxYear");
                    }
                );
                blueprint.Navigation(b => b.Reference).IsRequired();
            }
        );
        builder.Navigation(s => s.Blueprint).IsRequired();
    }
}
