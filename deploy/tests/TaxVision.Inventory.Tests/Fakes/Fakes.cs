using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using TaxVision.Inventory.Application.Abstractions;
using TaxVision.Inventory.Domain.Stock;
using TaxVision.Inventory.Domain.Suppliers;

namespace TaxVision.Inventory.Tests.Fakes;

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveChangesCallCount++;
        return Task.FromResult(0);
    }
}

internal sealed class FakeCorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; private set; } = string.Empty;

    public void Set(string correlationId) => CorrelationId = correlationId;

    public IDisposable Push(string correlationId)
    {
        var prev = CorrelationId;
        CorrelationId = correlationId;
        return new Popper(this, prev);
    }

    private sealed class Popper(FakeCorrelationContext o, string p) : IDisposable
    {
        public void Dispose() => o.CorrelationId = p;
    }
}

internal sealed class FakeStockRepository : IStockRepository
{
    public List<StockLevel> Levels { get; } = [];
    public List<StockMovement> Movements { get; } = [];

    public void Seed(StockLevel level) => Levels.Add(level);

    public Task<StockLevel?> GetByCatalogItemAsync(Guid tenantId, Guid catalogItemId, CancellationToken ct = default) =>
        Task.FromResult(Levels.FirstOrDefault(s => s.TenantId == tenantId && s.CatalogItemId == catalogItemId));

    public Task AddStockLevelAsync(StockLevel level, CancellationToken ct = default)
    {
        Levels.Add(level);
        return Task.CompletedTask;
    }

    public Task AddMovementAsync(StockMovement movement, CancellationToken ct = default)
    {
        Movements.Add(movement);
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<StockLevel> Items, int Total)> ListStockLevelsAsync(
        Guid tenantId,
        bool lowStockOnly,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        var q = Levels.Where(s => s.TenantId == tenantId);
        if (lowStockOnly)
            q = q.Where(s => s.IsLowStock);
        var all = q.ToList();
        IReadOnlyList<StockLevel> paged = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((paged, all.Count));
    }

    public Task<(IReadOnlyList<StockMovement> Items, int Total)> ListMovementsAsync(
        Guid tenantId,
        Guid? catalogItemId,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        var q = Movements.Where(m => m.TenantId == tenantId);
        if (catalogItemId is { } cid)
            q = q.Where(m => m.CatalogItemId == cid);
        var all = q.ToList();
        IReadOnlyList<StockMovement> paged = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((paged, all.Count));
    }
}

internal sealed class FakeSupplierRepository : ISupplierRepository
{
    public List<Supplier> Store { get; } = [];

    public void Seed(Supplier s) => Store.Add(s);

    public Task<Supplier?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Store.FirstOrDefault(s => s.TenantId == tenantId && !s.IsDeleted && s.Id == id));

    public Task<IReadOnlyList<Supplier>> ListAsync(Guid tenantId, bool activeOnly, CancellationToken ct = default)
    {
        var q = Store.Where(s => s.TenantId == tenantId && !s.IsDeleted);
        if (activeOnly)
            q = q.Where(s => s.IsActive);
        IReadOnlyList<Supplier> r = q.ToList();
        return Task.FromResult(r);
    }

    public Task AddAsync(Supplier supplier, CancellationToken ct = default)
    {
        Store.Add(supplier);
        return Task.CompletedTask;
    }
}

internal sealed class FakeItemSupplierRepository : IItemSupplierRepository
{
    public List<ItemSupplier> Store { get; } = [];

    public Task<ItemSupplier?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Store.FirstOrDefault(x => x.TenantId == tenantId && x.Id == id));

    public Task<ItemSupplier?> GetAsync(
        Guid tenantId,
        Guid catalogItemId,
        Guid supplierId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Store.FirstOrDefault(x =>
                x.TenantId == tenantId && x.CatalogItemId == catalogItemId && x.SupplierId == supplierId
            )
        );

    public Task<IReadOnlyList<ItemSupplier>> ListByItemAsync(
        Guid tenantId,
        Guid catalogItemId,
        CancellationToken ct = default
    )
    {
        IReadOnlyList<ItemSupplier> r = Store
            .Where(x => x.TenantId == tenantId && x.CatalogItemId == catalogItemId)
            .ToList();
        return Task.FromResult(r);
    }

    public Task AddAsync(ItemSupplier link, CancellationToken ct = default)
    {
        Store.Add(link);
        return Task.CompletedTask;
    }

    public void Remove(ItemSupplier link) => Store.Remove(link);
}
