using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tenant.Domain.Brands;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Infrastructure.Persistence.Configurations;

public sealed class TenantBrandColorConfiguration : IEntityTypeConfiguration<TenantBrandColor>
{
    public void Configure(EntityTypeBuilder<TenantBrandColor> b)
    {
        b.ToTable("TenantBrandColors");
        b.HasKey(x => x.Id);
        // La PK Guid la asigna el dominio (Guid.NewGuid en el factory). Sin esto, al agregar un color
        // NUEVO a un brand YA cargado/trackeado, EF ve el Guid ya seteado y lo trata como Modified
        // (UPDATE 0 filas → DbUpdateConcurrencyException) en vez de Added (INSERT) — guardrail #10.
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.TenantBrandId).IsRequired();
        b.Property(x => x.Token).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.UpdatedAtUtc).IsRequired();

        // HexColor VO ↔ nvarchar(7). Aquí es NO nullable (una fila existe solo si hay color).
        b.Property(x => x.Color)
            .HasConversion(color => color.Value, value => HexColor.Create(value).Value)
            .HasColumnName("HexValue")
            .HasMaxLength(7)
            .IsRequired();

        // Un token único por marca (no puede haber dos "Primary" en la misma superficie).
        b.HasIndex(x => new { x.TenantBrandId, x.Token }).IsUnique();
    }
}
