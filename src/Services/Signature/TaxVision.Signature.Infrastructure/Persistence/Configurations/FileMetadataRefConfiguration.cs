using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

internal sealed class FileMetadataRefConfiguration : IEntityTypeConfiguration<FileMetadataRef>
{
    public void Configure(EntityTypeBuilder<FileMetadataRef> builder)
    {
        builder.ToTable("FileMetadataRefs");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.FileId).IsRequired();
        builder.Property(p => p.ObjectKey).IsRequired().HasMaxLength(512);
        builder.Property(p => p.ContentType).IsRequired().HasMaxLength(200);
        builder.Property(p => p.SizeBytes).IsRequired();
        builder.Property(p => p.ChecksumSha256).HasMaxLength(64);
        builder.Property(p => p.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.RejectionReason).HasMaxLength(1024);
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.FileId }).IsUnique();
    }
}
