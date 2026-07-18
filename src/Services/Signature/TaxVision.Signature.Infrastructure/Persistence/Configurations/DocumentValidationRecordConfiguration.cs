using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Validation;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class DocumentValidationRecordConfiguration : IEntityTypeConfiguration<DocumentValidationRecord>
{
    public void Configure(EntityTypeBuilder<DocumentValidationRecord> builder)
    {
        builder.ToTable("DocumentValidationRecords");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.RequestedByUserId).IsRequired();
        builder.Property(r => r.ContentSha256).IsRequired().HasMaxLength(64);
        builder.Property(r => r.FileName).IsRequired().HasMaxLength(DocumentValidationRecord.MaxFileNameLength);
        builder.Property(r => r.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(r => r.SizeBytes).IsRequired();
        builder.Property(r => r.PageCount);
        builder.Property(r => r.HasExistingSignatures).IsRequired();
        builder.Property(r => r.Verdict).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(r => r.RejectionCode).HasMaxLength(80);
        builder.Property(r => r.RejectionReason).HasMaxLength(DocumentValidationRecord.MaxReasonLength);
        builder.Property(r => r.CreatedAtUtc).IsRequired();

        builder.HasIndex(r => new { r.TenantId, r.CreatedAtUtc });
        builder.HasIndex(r => new { r.TenantId, r.ContentSha256 });
    }
}
