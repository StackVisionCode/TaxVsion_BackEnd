using TaxVision.Inventory.Domain.Stock;
using TaxVision.Inventory.Domain.Suppliers;

namespace TaxVision.Inventory.Application.Abstractions;

// Reads con tenantId EXPLÍCITO + IgnoreQueryFilters en la implementación: dentro de un handler Wolverine
// el TenantContext ambiente no llega al DbContext (scopes de DI distintos). El tenant viene validado del
// JWT / del evento.
public interface IStockRepository
{
    Task<StockLevel?> GetByCatalogItemAsync(Guid tenantId, Guid catalogItemId, CancellationToken ct = default);

    /// <summary>Niveles de stock de un lote de ítems (para la venta de una factura). Change-tracked (NO
    /// AsNoTracking) porque el caller les aplica movimientos y persiste. Solo devuelve los que existen.</summary>
    Task<IReadOnlyList<StockLevel>> GetByCatalogItemsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> catalogItemIds,
        CancellationToken ct = default
    );

    /// <summary>Todos los movimientos con esta referencia (el id de factura). Con ellos el commit de la
    /// venta calcula el estado ACTUAL descontado por la factura y reconcilia por delta: así emitir, editar
    /// (cambia cantidades) y anular (repone todo) son la misma operación idempotente.</summary>
    Task<IReadOnlyList<StockMovement>> GetMovementsByReferenceAsync(
        Guid tenantId,
        string reference,
        CancellationToken ct = default
    );

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
