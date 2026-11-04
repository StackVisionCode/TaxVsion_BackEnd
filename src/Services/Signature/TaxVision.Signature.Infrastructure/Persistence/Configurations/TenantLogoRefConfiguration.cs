using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class TenantLogoRefConfiguration : IEntityTypeConfiguration<TenantLogoRef>
{
    public void Configure(EntityTypeBuilder<TenantLogoRef> builder)
    {
        builder.ToTable("TenantLogoRefs");
        // Un logo por superficie: retirar el del portal no toca el del CRM.
        builder.HasKey(l => new { l.TenantId, l.Surface });
        builder.Property(l => l.TenantId).ValueGeneratedNever();
        builder.Property(l => l.Surface).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(l => l.LogoContentType).HasMaxLength(100);
        builder.Property(l => l.UpdatedAtUtc).IsRequired();
    }
}
