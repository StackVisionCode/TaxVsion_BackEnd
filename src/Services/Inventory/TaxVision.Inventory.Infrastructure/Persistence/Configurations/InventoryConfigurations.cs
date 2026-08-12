using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Inventory.Domain.Stock;
using TaxVision.Inventory.Domain.Suppliers;

namespace TaxVision.Inventory.Infrastructure.Persistence.Configurations;

public sealed class StockLevelConfiguration : IEntityTypeConfiguration<StockLevel>
{
    public void Configure(EntityTypeBuilder<StockLevel> builder)
    {
        builder.ToTable("StockLevels");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.CatalogItemId).IsRequired();
        builder.Property(s => s.QuantityOnHand).IsRequired();
        builder.Property(s => s.MinLevel).IsRequired();
        builder.Property(s => s.MaxLevel).IsRequired();
        builder.Property(s => s.ReorderPoint).IsRequired();
        builder.Property(s => s.IsTracked).IsRequired();
        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.UpdatedAtUtc).IsRequired();

        builder.HasIndex(s => new { s.TenantId, s.CatalogItemId })
            .IsUnique()
            .HasDatabaseName("UX_StockLevels_Tenant_CatalogItem");
        builder.HasIndex(s => new { s.TenantId, s.IsTracked });
    }
}

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.TenantId).IsRequired();
        builder.Property(m => m.CatalogItemId).IsRequired();
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.Quantity).IsRequired();
        builder.Property(m => m.PreviousQuantity).IsRequired();
        builder.Property(m => m.NewQuantity).IsRequired();
        builder.Property(m => m.Reference).HasMaxLength(200);
        builder.Property(m => m.Notes).HasMaxLength(1000);
        builder.Property(m => m.MovedByUserId).IsRequired();
        builder.Property(m => m.MovedAtUtc).IsRequired();

        builder.HasIndex(m => new { m.TenantId, m.CatalogItemId, m.MovedAtUtc });
    }
}

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.TaxUserId).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(Supplier.NameMax).IsRequired();
        builder.Property(s => s.ContactName).HasMaxLength(200);
        builder.Property(s => s.Email).HasMaxLength(320);
        builder.Property(s => s.Phone).HasMaxLength(50);
        builder.Property(s => s.Address).HasMaxLength(500);
        builder.Property(s => s.TaxId).HasMaxLength(50);
        builder.Property(s => s.IsActive).IsRequired();
        builder.Property(s => s.IsDeleted).IsRequired();
        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.UpdatedAtUtc).IsRequired();

        builder.HasIndex(s => new { s.TenantId, s.IsActive });
    }
}

public sealed class ItemSupplierConfiguration : IEntityTypeConfiguration<ItemSupplier>
{
    public void Configure(EntityTypeBuilder<ItemSupplier> builder)
    {
        builder.ToTable("ItemSuppliers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.CatalogItemId).IsRequired();
        builder.Property(x => x.SupplierId).IsRequired();
        builder.Property(x => x.SupplierSku).HasMaxLength(100);
        builder.Property(x => x.LeadTimeDays);
        builder.Property(x => x.IsPreferred).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.OwnsOne(x => x.SupplierPrice, price =>
        {
            price.Property(p => p.Amount).HasColumnName("SupplierPrice_Amount").HasColumnType("decimal(18,2)");
            price.Property(p => p.Currency).HasColumnName("SupplierPrice_Currency").HasMaxLength(3);
        });

        builder.HasIndex(x => new { x.TenantId, x.CatalogItemId, x.SupplierId })
            .IsUnique()
            .HasDatabaseName("UX_ItemSuppliers_Tenant_Item_Supplier");
        builder.HasIndex(x => new { x.TenantId, x.SupplierId });
    }
}
