using TaxVision.Inventory.Domain.Stock;
using TaxVision.Inventory.Domain.Suppliers;

namespace TaxVision.Inventory.Application.Abstractions;

// Reads con tenantId EXPLÍCITO + IgnoreQueryFilters en la implementación: dentro de un handler Wolverine
// el TenantContext ambiente no llega al DbContext (scopes de DI distintos). El tenant viene validado del
// JWT / del evento.
public interface IStockRepository
{
    Task<StockLevel?> GetByCatalogItemAsync(Guid tenantId, Guid catalogItemId, CancellationToken ct = default);

    Task AddStockLevelAsync(StockLevel level, CancellationToken ct = default);

    Task AddMovementAsync(StockMovement movement, CancellationToken ct = default);

    Task<(IReadOnlyList<StockLevel> Items, int Total)> ListStockLevelsAsync(
        Guid tenantId,
        bool lowStockOnly,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<(IReadOnlyList<StockMovement> Items, int Total)> ListMovementsAsync(
        Guid tenantId,
        Guid? catalogItemId,
        int page,
        int pageSize,
        CancellationToken ct = default
    );
}

public interface ISupplierRepository
{
    Task<Supplier?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Supplier>> ListAsync(Guid tenantId, bool activeOnly, CancellationToken ct = default);

    Task AddAsync(Supplier supplier, CancellationToken ct = default);
}

public interface IItemSupplierRepository
{
    Task<ItemSupplier?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<ItemSupplier?> GetAsync(Guid tenantId, Guid catalogItemId, Guid supplierId, CancellationToken ct = default);

    Task<IReadOnlyList<ItemSupplier>> ListByItemAsync(
        Guid tenantId,
        Guid catalogItemId,
        CancellationToken ct = default
    );

    Task AddAsync(ItemSupplier link, CancellationToken ct = default);

    void Remove(ItemSupplier link);
}
