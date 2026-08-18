using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Infrastructure.Persistence.Configurations;

public sealed class TaskAttachmentConfiguration : IEntityTypeConfiguration<TaskAttachment>
{
    public void Configure(EntityTypeBuilder<TaskAttachment> builder)
    {
        builder.ToTable("TaskAttachments");
        builder.HasKey(a => a.Id);

        // El Guid lo genera el dominio: sin esto EF intenta un UPDATE en vez de un INSERT.
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.TaskId).IsRequired();
        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.FileId).IsRequired();
        builder.Property(a => a.DisplayName).HasMaxLength(260).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(160);
        builder.Property(a => a.SizeBytes).IsRequired();
        builder.Property(a => a.Origin).HasConversion<int>().IsRequired();
        builder.Property(a => a.Status).HasConversion<int>().IsRequired();
        builder.Property(a => a.RejectionReason).HasMaxLength(200);
        builder.Property(a => a.AttachedByUserId).IsRequired();
        builder.Property(a => a.AttachedAtUtc).IsRequired();
        builder.Property(a => a.DetachedAtUtc);

        builder.Ignore(a => a.IsActive);

        // El mismo archivo no se adjunta dos veces vivo, pero sí se puede volver a adjuntar después
        // de desadjuntarlo: por eso el único es filtrado y no total.
        builder
            .HasIndex(a => new { a.TaskId, a.FileId })
            .IsUnique()
            .HasFilter($"[Status] <> {(int)AttachmentStatus.Detached}")
            .HasDatabaseName("UX_TaskAttachments_TaskId_FileId_Active");

        // El consumer de CloudStorage llega con un FileId y nada más; sin este índice resuelve el
        // adjunto escaneando la tabla entera del tenant.
        builder.HasIndex(a => new { a.TenantId, a.FileId }).HasDatabaseName("IX_TaskAttachments_TenantId_FileId");
    }
}
