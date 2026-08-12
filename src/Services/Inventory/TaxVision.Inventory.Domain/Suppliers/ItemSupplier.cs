using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Inventory.Domain.ValueObjects;

namespace TaxVision.Inventory.Domain.Suppliers;

/// <summary>Vínculo ítem-de-catálogo ↔ proveedor, con precio y SKU del proveedor. El ítem es una
/// referencia débil (<see cref="CatalogItemId"/>, sin FK cross-service).</summary>
public sealed class ItemSupplier : TenantEntity
{
    public Guid CatalogItemId { get; private set; }
    public Guid SupplierId { get; private set; }
    public string? SupplierSku { get; private set; }
    public Money? SupplierPrice { get; private set; }
    public int? LeadTimeDays { get; private set; }
    public bool IsPreferred { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private ItemSupplier() { }

    public static Result<ItemSupplier> Create(
        Guid tenantId,
        Guid catalogItemId,
        Guid supplierId,
        string? supplierSku,
        Money? supplierPrice,
        int? leadTimeDays,
        bool isPreferred,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<ItemSupplier>(InventoryErrors.InvalidTenant);
        if (catalogItemId == Guid.Empty)
            return Result.Failure<ItemSupplier>(InventoryErrors.InvalidCatalogItem);
        if (supplierId == Guid.Empty)
            return Result.Failure<ItemSupplier>(InventoryErrors.SupplierNotFound);

        var link = new ItemSupplier
        {
            CatalogItemId = catalogItemId,
            SupplierId = supplierId,
            SupplierSku = string.IsNullOrWhiteSpace(supplierSku) ? null : supplierSku.Trim(),
            SupplierPrice = supplierPrice,
            LeadTimeDays = leadTimeDays is > 0 ? leadTimeDays : null,
            IsPreferred = isPreferred,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        link.SetTenant(tenantId);
        return Result.Success(link);
    }

    public void Update(string? supplierSku, Money? supplierPrice, int? leadTimeDays, bool isPreferred, DateTime nowUtc)
    {
        SupplierSku = string.IsNullOrWhiteSpace(supplierSku) ? null : supplierSku.Trim();
        SupplierPrice = supplierPrice;
        LeadTimeDays = leadTimeDays is > 0 ? leadTimeDays : null;
        IsPreferred = isPreferred;
        UpdatedAtUtc = nowUtc;
    }
}
