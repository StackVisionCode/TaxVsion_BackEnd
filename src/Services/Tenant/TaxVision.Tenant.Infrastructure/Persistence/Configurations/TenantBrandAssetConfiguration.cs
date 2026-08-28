using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tenant.Domain.Brands;

namespace TaxVision.Tenant.Infrastructure.Persistence.Configurations;

public sealed class TenantBrandAssetConfiguration : IEntityTypeConfiguration<TenantBrandAsset>
{
    public void Configure(EntityTypeBuilder<TenantBrandAsset> b)
    {
        b.ToTable("TenantBrandAssets");
        b.HasKey(x => x.Id);
        // PK asignada por el dominio: sin ValueGeneratedNever, agregar un asset NUEVO a un brand ya
        // cargado se trata como Modified (UPDATE 0 filas) en vez de Added — guardrail #10.
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.TenantBrandId).IsRequired();
        b.Property(x => x.Key).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.FileId).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.SizeBytes).IsRequired();
        b.Property(x => x.Width);
        b.Property(x => x.Height);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.ConfirmedAtUtc);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc).IsRequired();

        // Un asset único por clave por marca (un logo y un favicon activos por superficie).
        b.HasIndex(x => new { x.TenantBrandId, x.Key }).IsUnique();
    }
}
