using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Configurations;

internal sealed class IncomingEmailAttachmentConfiguration : IEntityTypeConfiguration<IncomingEmailAttachment>
{
    public void Configure(EntityTypeBuilder<IncomingEmailAttachment> builder)
    {
        builder.ToTable("IncomingEmailAttachments");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.IncomingEmailId).IsRequired();
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Filename).IsRequired().HasMaxLength(IncomingEmailAttachment.FilenameMaxLength);
        builder.Property(x => x.ContentType).IsRequired().HasMaxLength(IncomingEmailAttachment.ContentTypeMaxLength);
        builder.Property(x => x.SizeBytes).IsRequired();
        builder
            .Property(x => x.ProviderAttachmentId)
            .IsRequired()
            .HasMaxLength(IncomingEmailAttachment.ProviderAttachmentIdMaxLength);
        builder.Property(x => x.IsInline).IsRequired();
        builder.Property(x => x.DownloadStatus).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.CloudStorageFileId);
        builder.Property(x => x.DownloadedAtUtc);
        builder.Property(x => x.FailureReason).HasMaxLength(IncomingEmailAttachment.FailureReasonMaxLength);

        // Alimenta el job de retry de Fase 12 (buscar todos los Failed para reintentar).
        builder.HasIndex(x => x.DownloadStatus).HasDatabaseName("IX_IncomingEmailAttachments_DownloadStatus");
    }
}
