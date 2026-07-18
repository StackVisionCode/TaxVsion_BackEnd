using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Correspondence.Domain.Audit;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Configurations;

/// <summary>Append-only — sin lado de lectura todavía (ver comentario de clase de <see cref="CorrespondenceAuditLog"/>), por eso solo el índice de partición por tenant, nada pensado para un patrón de consulta que aún no existe (YAGNI).</summary>
internal sealed class CorrespondenceAuditLogConfiguration : IEntityTypeConfiguration<CorrespondenceAuditLog>
{
    public void Configure(EntityTypeBuilder<CorrespondenceAuditLog> builder)
    {
        builder.ToTable("CorrespondenceAuditLogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Action).IsRequired().HasMaxLength(CorrespondenceAuditLog.ActionMaxLength);
        builder.Property(x => x.TargetType).IsRequired().HasMaxLength(CorrespondenceAuditLog.TargetTypeMaxLength);
        builder.Property(x => x.TargetId).IsRequired();
        builder.Property(x => x.UserId);
        builder.Property(x => x.CorrelationId).IsRequired().HasMaxLength(CorrespondenceAuditLog.CorrelationIdMaxLength);
        builder.Property(x => x.Detail).IsRequired().HasMaxLength(CorrespondenceAuditLog.DetailMaxLength);
        builder.Property(x => x.TimestampUtc).IsRequired();

        builder
            .HasIndex(x => new { x.TenantId, x.TargetId })
            .HasDatabaseName("IX_CorrespondenceAuditLogs_TenantId_TargetId");
    }
}
