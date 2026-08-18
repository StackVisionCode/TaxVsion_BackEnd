using Microsoft.EntityFrameworkCore;
using TaxVision.Inventory.Application.Abstractions;
using TaxVision.Inventory.Domain.Stock;
using TaxVision.Inventory.Domain.Suppliers;

namespace TaxVision.Inventory.Infrastructure.Persistence.Repositories;

// Reads con IgnoreQueryFilters() + tenantId explícito: dentro de un handler/consumer Wolverine el
// TenantContext ambiente no llega al DbContext (scopes de DI distintos). El tenant viene validado del
// JWT / del evento.
public sealed class StockRepository(InventoryDbContext db) : IStockRepository
{
    public Task<StockLevel?> GetByCatalogItemAsync(Guid tenantId, Guid catalogItemId, CancellationToken ct = default) =>
        db
            .StockLevels.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.CatalogItemId == catalogItemId, ct);

    public async Task AddStockLevelAsync(StockLevel level, CancellationToken ct = default) =>
        await db.StockLevels.AddAsync(level, ct);

    public async Task AddMovementAsync(StockMovement movement, CancellationToken ct = default) =>
        await db.StockMovements.AddAsync(movement, ct);

    public async Task<(IReadOnlyList<StockLevel> Items, int Total)> ListStockLevelsAsync(
        Guid tenantId,
        bool lowStockOnly,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        var query = db.StockLevels.IgnoreQueryFilters().AsNoTracking().Where(s => s.TenantId == tenantId);
        if (lowStockOnly)
            query = query.Where(s => s.IsTracked && s.QuantityOnHand <= s.MinLevel);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(s => s.CatalogItemId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (rows, total);
    }

    public async Task<(IReadOnlyList<StockMovement> Items, int Total)> ListMovementsAsync(
        Guid tenantId,
        Guid? catalogItemId,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        var query = db.StockMovements.IgnoreQueryFilters().AsNoTracking().Where(m => m.TenantId == tenantId);
        if (catalogItemId is { } cid)
            query = query.Where(m => m.CatalogItemId == cid);
        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(m => m.MovedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (rows, total);
    }
}

public sealed class SupplierRepository(InventoryDbContext db) : ISupplierRepository
{
    public Task<Supplier?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db
            .Suppliers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && !s.IsDeleted && s.Id == id, ct);

    public async Task<IReadOnlyList<Supplier>> ListAsync(Guid tenantId, bool activeOnly, CancellationToken ct = default)
    {
        var query = db.Suppliers.IgnoreQueryFilters().AsNoTracking().Where(s => s.TenantId == tenantId && !s.IsDeleted);
        if (activeOnly)
            query = query.Where(s => s.IsActive);
        return await query.OrderBy(s => s.Name).ToListAsync(ct);
    }

    public async Task AddAsync(Supplier supplier, CancellationToken ct = default) =>
        await db.Suppliers.AddAsync(supplier, ct);
}

public sealed class ItemSupplierRepository(InventoryDbContext db) : IItemSupplierRepository
{
    public Task<ItemSupplier?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db.ItemSuppliers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public Task<ItemSupplier?> GetAsync(
        Guid tenantId,
        Guid catalogItemId,
        Guid supplierId,
        CancellationToken ct = default
    ) =>
        db
            .ItemSuppliers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId && x.CatalogItemId == catalogItemId && x.SupplierId == supplierId,
                ct
            );

    public async Task<IReadOnlyList<ItemSupplier>> ListByItemAsync(
        Guid tenantId,
        Guid catalogItemId,
        CancellationToken ct = default
    ) =>
        await db
            .ItemSuppliers.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.CatalogItemId == catalogItemId)
            .ToListAsync(ct);

    public async Task AddAsync(ItemSupplier link, CancellationToken ct = default) =>
        await db.ItemSuppliers.AddAsync(link, ct);

    public void Remove(ItemSupplier link) => db.ItemSuppliers.Remove(link);
}
