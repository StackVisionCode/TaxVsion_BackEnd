using BuildingBlocks.Results;

namespace TaxVision.Inventory.Domain;

/// <summary>Códigos de error canónicos del dominio Inventory.</summary>
public static class InventoryErrors
{
    public static Error InvalidTenant => new("inventory.invalidTenant", "TenantId is required.");
    public static Error InvalidCatalogItem => new("inventory.invalidCatalogItem", "A valid CatalogItemId is required.");
    public static Error InvalidName =>
        new("inventory.invalidName", "Name is required and must be within the max length.");
    public static Error InvalidQuantity => new("inventory.invalidQuantity", "Quantity must be a positive number.");
    public static Error InvalidAmount => new("inventory.invalidAmount", "Amount must be zero or positive.");
    public static Error InvalidCurrency =>
        new("inventory.invalidCurrency", "Currency must be a 3-letter ISO 4217 code.");

    public static Error InsufficientStock =>
        new("inventory.insufficientStock", "The movement would drive stock below zero.");
    public static Error NotTracked => new("inventory.notTracked", "This item does not track inventory.");

    public static Error StockLevelNotFound => new("inventory.stockLevelNotFound", "No stock level found for the item.");
    public static Error SupplierNotFound => new("inventory.supplierNotFound", "Supplier not found.");
    public static Error ItemSupplierNotFound => new("inventory.itemSupplierNotFound", "Item-supplier link not found.");
}
