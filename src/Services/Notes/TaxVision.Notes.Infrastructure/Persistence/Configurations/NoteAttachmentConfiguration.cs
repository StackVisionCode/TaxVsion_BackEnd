using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Infrastructure.Persistence.Configurations;

public sealed class NoteAttachmentConfiguration : IEntityTypeConfiguration<NoteAttachment>
{
    public void Configure(EntityTypeBuilder<NoteAttachment> builder)
    {
        builder.ToTable("NoteAttachments");
        builder.HasKey(a => a.Id);

        // Guardrail 10: Id es un Guid generado en dominio (NoteAttachment.Create) — sin esto EF
        // haría UPDATE en vez de INSERT al agregar adjuntos nuevos a una Note ya trackeada.
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.NoteId).IsRequired();
        builder.Property(a => a.CloudStorageFileId).IsRequired();
        builder.Property(a => a.DisplayName).HasMaxLength(260).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(200).IsRequired();
        builder.Property(a => a.SizeBytes).IsRequired();
        builder.Property(a => a.Status).HasConversion<int>().IsRequired();
        builder.Property(a => a.RejectionReason).HasMaxLength(500);
        builder.Property(a => a.LinkedAtUtc).IsRequired();

        builder.HasIndex(a => new { a.NoteId, a.CloudStorageFileId }).IsUnique();
    }
}
