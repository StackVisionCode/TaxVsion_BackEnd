using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Documents.Domain.Branding;

namespace TaxVision.Documents.Infrastructure.Persistence.Configurations;

public sealed class TenantLogoRefConfiguration : IEntityTypeConfiguration<TenantLogoRef>
{
    public void Configure(EntityTypeBuilder<TenantLogoRef> builder)
    {
        builder.ToTable("TenantLogoRefs");
        // Un logo por superficie: quitar el del portal no toca el del CRM.
        builder.HasKey(x => new { x.TenantId, x.Surface });
        builder.Property(x => x.TenantId).ValueGeneratedNever();
        builder.Property(x => x.Surface).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.LogoContentType).HasMaxLength(100);
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
    }
}
