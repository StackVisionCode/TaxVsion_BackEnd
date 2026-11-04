using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Configurations;

internal sealed class DraftAttachmentConfiguration : IEntityTypeConfiguration<DraftAttachment>
{
    public void Configure(EntityTypeBuilder<DraftAttachment> builder)
    {
        builder.ToTable("DraftAttachments");

        builder.HasKey(x => x.Id);
        // Id lo asigna el dominio: ValueGeneratedNever para que EF lo inserte (no UPDATE) al agregarlo a un Draft cargado.
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.DraftId).IsRequired();
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.FileId).IsRequired();
        builder.Property(x => x.Filename).IsRequired().HasMaxLength(DraftAttachmentRef.FilenameMaxLength);
        builder.Property(x => x.ContentType).IsRequired().HasMaxLength(DraftAttachmentRef.ContentTypeMaxLength);
        builder.Property(x => x.SizeBytes).IsRequired();

        // Un archivo una sola vez por borrador (Draft.AttachFile ya lo trata como no-op).
        builder
            .HasIndex(x => new { x.DraftId, x.FileId })
            .IsUnique()
            .HasDatabaseName("IX_DraftAttachments_DraftId_FileId_Unique");

        // Búsqueda de archivos (/correspondence/search): LIKE acotado por tenant (D-6).
        builder
            .HasIndex(x => new { x.TenantId, x.DraftId })
            .IncludeProperties(x => x.Filename)
            .HasDatabaseName("IX_DraftAttachments_TenantId_DraftId");
    }
}
