using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tenant.Domain.Brands;

namespace TaxVision.Tenant.Infrastructure.Persistence.Configurations;

public sealed class TenantBrandConfiguration : IEntityTypeConfiguration<TenantBrand>
{
    public void Configure(EntityTypeBuilder<TenantBrand> b)
    {
        b.ToTable("TenantBrands");
        b.HasKey(x => x.Id);
        // PK asignada por el dominio (Guid.NewGuid en Create) — guardrail #10, consistente con los hijos.
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.Surface).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc).IsRequired();

        // Una marca por tenant por superficie.
        b.HasIndex(x => new { x.TenantId, x.Surface }).IsUnique();

        // Colecciones hijas mapeadas por backing field (patrón Customer): EF navega por la propiedad
        // de solo lectura pero lee/escribe el List privado. Cascade: la marca es dueña de sus hijos.
        b.HasMany(x => x.Colors).WithOne().HasForeignKey(c => c.TenantBrandId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Colors).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasMany(x => x.Assets).WithOne().HasForeignKey(a => a.TenantBrandId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Assets).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
