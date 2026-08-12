using TaxVision.Inventory.Domain.Stock;
using TaxVision.Inventory.Domain.Suppliers;

namespace TaxVision.Inventory.Application.Common;

public sealed record MoneyDto(decimal Amount, string Currency);

public sealed record StockLevelDto(
    Guid CatalogItemId,
    int QuantityOnHand,
    int MinLevel,
    int MaxLevel,
    int ReorderPoint,
    bool IsTracked,
    bool IsLowStock,
    DateTime UpdatedAtUtc
);

public sealed record StockMovementDto(
    Guid Id,
    Guid CatalogItemId,
    string Type,
    int Quantity,
    int PreviousQuantity,
    int NewQuantity,
    string? Reference,
    string? Notes,
    DateTime MovedAtUtc
);

public sealed record SupplierDto(
    Guid Id,
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Address,
    string? TaxId,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc
);

public sealed record ItemSupplierDto(
    Guid Id,
    Guid CatalogItemId,
    Guid SupplierId,
    string? SupplierSku,
    MoneyDto? SupplierPrice,
    int? LeadTimeDays,
    bool IsPreferred
);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public static class InventoryMappings
{
    public static StockLevelDto ToDto(this StockLevel s) =>
        new(s.CatalogItemId, s.QuantityOnHand, s.MinLevel, s.MaxLevel, s.ReorderPoint, s.IsTracked, s.IsLowStock, s.UpdatedAtUtc);

    public static StockMovementDto ToDto(this StockMovement m) =>
        new(m.Id, m.CatalogItemId, m.Type.ToString(), m.Quantity, m.PreviousQuantity, m.NewQuantity, m.Reference, m.Notes, m.MovedAtUtc);

    public static SupplierDto ToDto(this Supplier s) =>
        new(s.Id, s.Name, s.ContactName, s.Email, s.Phone, s.Address, s.TaxId, s.IsActive, s.CreatedAtUtc, s.UpdatedAtUtc);

    public static ItemSupplierDto ToDto(this ItemSupplier x) =>
        new(x.Id, x.CatalogItemId, x.SupplierId, x.SupplierSku,
            x.SupplierPrice is null ? null : new MoneyDto(x.SupplierPrice.Amount, x.SupplierPrice.Currency),
            x.LeadTimeDays, x.IsPreferred);
}
