using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class SignerDocumentCompletionConfiguration : IEntityTypeConfiguration<SignerDocumentCompletion>
{
    public void Configure(EntityTypeBuilder<SignerDocumentCompletion> builder)
    {
        builder.ToTable("SignerDocumentCompletions");
        builder.HasKey(completion => completion.Id);
        builder.Property(completion => completion.Id).ValueGeneratedNever();
        builder.Property(completion => completion.SignerId).IsRequired();
        builder.Property(completion => completion.DocumentId).IsRequired();
        builder.Property(completion => completion.CompletedAtUtc).IsRequired();
        builder.Property(completion => completion.PartialCopyRequestedAtUtc);
        builder.Property(completion => completion.PartialCopySentAtUtc);
        builder.Property(completion => completion.PartialCopyFileId);
        builder.Property(completion => completion.PartialCopyFailureReason).HasMaxLength(500);
        builder.HasIndex(completion => new { completion.SignerId, completion.DocumentId }).IsUnique();
        builder.HasIndex(completion => completion.DocumentId);
    }
}
