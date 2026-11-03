using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Infrastructure.Persistence.Configurations;

public sealed class TenantLogoRefConfiguration : IEntityTypeConfiguration<TenantLogoRef>
{
    public void Configure(EntityTypeBuilder<TenantLogoRef> builder)
    {
        builder.ToTable("TenantLogoRefs");
        // Un logo por superficie: retirar el del portal no toca el del CRM.
        builder.HasKey(r => new { r.TenantId, r.Surface });
        builder.Property(r => r.TenantId).ValueGeneratedNever();
        builder.Property(r => r.Surface).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(r => r.CloudStorageFileId).IsRequired();
        builder.Property(r => r.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(r => r.SizeBytes).IsRequired();
        builder.Property(r => r.UpdatedAtUtc).IsRequired();
    }
}
