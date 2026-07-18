using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Infrastructure.Persistence.Configurations;

public sealed class TenantLogoRefConfiguration : IEntityTypeConfiguration<TenantLogoRef>
{
    public void Configure(EntityTypeBuilder<TenantLogoRef> builder)
    {
        builder.ToTable("TenantLogoRefs");
        builder.HasKey(r => r.TenantId);
        builder.Property(r => r.TenantId).ValueGeneratedNever();

        builder.Property(r => r.CloudStorageFileId).IsRequired();
        builder.Property(r => r.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(r => r.SizeBytes).IsRequired();
        builder.Property(r => r.UpdatedAtUtc).IsRequired();
    }
}
