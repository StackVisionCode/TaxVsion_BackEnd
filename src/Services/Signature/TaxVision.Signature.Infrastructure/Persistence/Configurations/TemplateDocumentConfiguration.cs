using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Templates;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class TemplateDocumentConfiguration : IEntityTypeConfiguration<TemplateDocument>
{
    public void Configure(EntityTypeBuilder<TemplateDocument> builder)
    {
        builder.ToTable("TemplateDocuments");
        builder.HasKey(document => document.Id);
        builder.Property(document => document.Id).ValueGeneratedNever();
        builder.Property(document => document.SignatureTemplateId).IsRequired();
        builder.Property(document => document.Order).IsRequired();
        builder.Property(document => document.FileId).IsRequired();
        builder.Property(document => document.Title).HasMaxLength(TemplateDocument.MaxTitleLength).IsRequired();
        builder.HasIndex(document => new { document.SignatureTemplateId, document.Order }).IsUnique();
        builder.HasIndex(document => new { document.SignatureTemplateId, document.FileId }).IsUnique();
    }
}
