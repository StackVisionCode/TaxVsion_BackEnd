using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Catalog.Domain.Items;

namespace TaxVision.Catalog.Infrastructure.Persistence.Configurations;

public sealed class CatalogItemConfiguration : IEntityTypeConfiguration<CatalogItem>
{
    public void Configure(EntityTypeBuilder<CatalogItem> builder)
    {
        builder.ToTable("CatalogItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.TenantId).IsRequired();
        builder.Property(i => i.TaxUserId).IsRequired();
        builder.Property(i => i.Name).HasMaxLength(CatalogItem.NameMax).IsRequired();
        builder.Property(i => i.Description).HasMaxLength(1000);
        builder.Property(i => i.Sku).HasMaxLength(CatalogItem.SkuMax);
        builder.Property(i => i.Barcode).HasMaxLength(100);
        builder.Property(i => i.CategoryId).IsRequired();
        builder.Property(i => i.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.Unit).HasMaxLength(50);
        builder.Property(i => i.ImageUrl).HasMaxLength(2000);
        builder.Property(i => i.TrackInventory).IsRequired();
        builder.Property(i => i.IsActive).IsRequired();
        builder.Property(i => i.IsDeleted).IsRequired();
        builder.Property(i => i.CreatedAtUtc).IsRequired();
        builder.Property(i => i.UpdatedAtUtc).IsRequired();

        // Precio (multi-moneda) como owned type → columnas Price_Amount / Price_Currency.
        builder.OwnsOne(i => i.Price, price =>
        {
            price.Property(p => p.Amount).HasColumnName("Price_Amount").HasColumnType("decimal(18,2)").IsRequired();
            price.Property(p => p.Currency).HasColumnName("Price_Currency").HasMaxLength(3).IsRequired();
        });
        builder.Navigation(i => i.Price).IsRequired();

        // Costo opcional (owned nullable) → Cost_Amount / Cost_Currency.
        builder.OwnsOne(i => i.Cost, cost =>
        {
            cost.Property(c => c.Amount).HasColumnName("Cost_Amount").HasColumnType("decimal(18,2)");
            cost.Property(c => c.Currency).HasColumnName("Cost_Currency").HasMaxLength(3);
        });

        // Atributos (EAV) como colección hija.
        builder.HasMany(i => i.Attributes).WithOne().HasForeignKey(a => a.CatalogItemId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(CatalogItem.Attributes))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        // Unicidad de SKU por tenant: índice único FILTRADO (solo activos con SKU). La garantía real.
        builder.HasIndex(i => new { i.TenantId, i.Sku })
            .IsUnique()
            .HasFilter("[Sku] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName("UX_CatalogItems_Tenant_Sku");

        builder.HasIndex(i => new { i.TenantId, i.CategoryId });
        builder.HasIndex(i => new { i.TenantId, i.IsActive });
    }
}
