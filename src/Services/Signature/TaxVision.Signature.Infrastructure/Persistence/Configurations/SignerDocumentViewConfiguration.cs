using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class SignerDocumentViewConfiguration : IEntityTypeConfiguration<SignerDocumentView>
{
    public void Configure(EntityTypeBuilder<SignerDocumentView> builder)
    {
        builder.ToTable("SignerDocumentViews");
        builder.HasKey(view => view.Id);
        builder.Property(view => view.Id).ValueGeneratedNever();
        builder.Property(view => view.SignerId).IsRequired();
        builder.Property(view => view.DocumentId).IsRequired();
        builder.Property(view => view.FirstViewedAtUtc).IsRequired();
        builder.HasIndex(view => new { view.SignerId, view.DocumentId }).IsUnique();
        builder.HasIndex(view => view.DocumentId);
    }
}
