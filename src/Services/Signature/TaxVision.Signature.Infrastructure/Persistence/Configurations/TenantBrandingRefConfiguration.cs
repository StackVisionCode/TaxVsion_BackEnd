using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class TenantBrandingRefConfiguration : IEntityTypeConfiguration<TenantBrandingRef>
{
    public void Configure(EntityTypeBuilder<TenantBrandingRef> builder)
    {
        builder.ToTable("TenantBrandingRefs");
        builder.HasKey(r => r.TenantId);
        builder.Property(r => r.TenantId).ValueGeneratedNever();

        builder.Property(r => r.OfficeName).HasMaxLength(256).IsRequired();
        builder.Property(r => r.LogoContentType).HasMaxLength(100);
        builder.Property(r => r.UpdatedAtUtc).IsRequired();
    }
}
