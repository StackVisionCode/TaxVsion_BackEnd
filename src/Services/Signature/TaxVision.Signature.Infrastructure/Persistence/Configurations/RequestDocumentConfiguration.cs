using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class RequestDocumentConfiguration : IEntityTypeConfiguration<RequestDocument>
{
    public void Configure(EntityTypeBuilder<RequestDocument> builder)
    {
        builder.ToTable("RequestDocuments");
        builder.HasKey(document => document.Id);
        builder.Property(document => document.Id).ValueGeneratedNever();

        builder.Property(document => document.SignatureRequestId).IsRequired();
        builder.Property(document => document.Order).IsRequired();
        builder.Property(document => document.Title).HasMaxLength(RequestDocument.MaxTitleLength).IsRequired();
        builder.Property(document => document.OriginalFileId).IsRequired();
        builder.Property(document => document.SealedFileId);
        builder.Property(document => document.CertificateFileId);
        builder.Property(document => document.SealedAtUtc);
        builder.Property(document => document.Note).HasMaxLength(RequestDocument.MaxNoteLength);

        builder.OwnsOne(
            document => document.DocumentHashPre,
            hash =>
                hash.Property(value => value.Value)
                    .HasColumnName("DocumentHashPre")
                    .HasMaxLength(DocumentHash.ExpectedLength)
        );
        builder.OwnsOne(
            document => document.DocumentHashPost,
            hash =>
                hash.Property(value => value.Value)
                    .HasColumnName("DocumentHashPost")
                    .HasMaxLength(DocumentHash.ExpectedLength)
        );

        builder.HasIndex(document => new { document.SignatureRequestId, document.Order }).IsUnique();
        builder.HasIndex(document => document.OriginalFileId);
        builder.HasIndex(document => document.SealedFileId);
        builder.HasIndex(document => document.CertificateFileId);
    }
}
