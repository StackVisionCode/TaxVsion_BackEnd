using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Infrastructure.Persistence.Configurations;

public sealed class SystemAssetRefConfiguration : IEntityTypeConfiguration<SystemAssetRef>
{
    public void Configure(EntityTypeBuilder<SystemAssetRef> builder)
    {
        builder.ToTable("SystemAssetRefs");
        builder.HasKey(r => r.Key);
        builder.Property(r => r.Key).HasMaxLength(100).ValueGeneratedNever();

        builder.Property(r => r.CloudStorageFileId).IsRequired();
        builder.Property(r => r.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(r => r.SizeBytes).IsRequired();
        builder.Property(r => r.UpdatedAtUtc).IsRequired();
    }
}
