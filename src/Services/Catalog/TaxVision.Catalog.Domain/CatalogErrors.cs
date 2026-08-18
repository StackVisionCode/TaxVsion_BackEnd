using BuildingBlocks.Results;

namespace TaxVision.Catalog.Domain;

/// <summary>Códigos de error canónicos del dominio Catalog (estables; viajan al caller).</summary>
public static class CatalogErrors
{
    public static Error InvalidTenant => new("catalog.invalidTenant", "TenantId is required.");
    public static Error InvalidCategory => new("catalog.invalidCategory", "A valid CategoryId is required.");
    public static Error InvalidName =>
        new("catalog.invalidName", "Name is required and must be within the max length.");
    public static Error InvalidSku => new("catalog.invalidSku", "SKU exceeds the maximum length.");
    public static Error InvalidAmount => new("catalog.invalidAmount", "Amount must be zero or positive.");
    public static Error InvalidCurrency => new("catalog.invalidCurrency", "Currency must be a 3-letter ISO 4217 code.");
    public static Error InvalidAttribute => new("catalog.invalidAttribute", "Attribute key and value are required.");

    public static Error ItemNotFound => new("catalog.itemNotFound", "Catalog item not found.");
    public static Error CategoryNotFound => new("catalog.categoryNotFound", "Category not found.");
    public static Error DuplicateSku => new("catalog.duplicateSku", "An item with the same SKU already exists.");
    public static Error CategoryHasChildren =>
        new("catalog.categoryHasChildren", "The category has subcategories or items.");
}
