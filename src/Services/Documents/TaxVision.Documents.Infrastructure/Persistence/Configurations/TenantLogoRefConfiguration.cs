using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Documents.Domain.Branding;

namespace TaxVision.Documents.Infrastructure.Persistence.Configurations;

public sealed class TenantLogoRefConfiguration : IEntityTypeConfiguration<TenantLogoRef>
{
    public void Configure(EntityTypeBuilder<TenantLogoRef> builder)
    {
        builder.ToTable("TenantLogoRefs");
        builder.HasKey(x => x.TenantId);
        builder.Property(x => x.TenantId).ValueGeneratedNever();
        builder.Property(x => x.LogoContentType).HasMaxLength(100);
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
    }
}
