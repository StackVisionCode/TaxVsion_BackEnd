using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Inventory.Application.Abstractions;
using TaxVision.Inventory.Application.Stock;
using TaxVision.Inventory.Domain;
using TaxVision.Inventory.Domain.Stock;
using TaxVision.Inventory.Infrastructure.Persistence;
using TaxVision.Inventory.Infrastructure.Persistence.Repositories;

namespace TaxVision.Inventory.Tests.Integration;

/// <summary>
/// Contra el SQL Server real (INVENTORY_DB_CONNECTION o user-secrets de Inventory): ajustes y ventas del
/// mismo producto en paralelo no pierden movimientos, y la misma factura no se descuenta dos veces. Cada
/// operación abre su transacción, como la de Wolverine. Tenant propio; se limpia al terminar.
/// </summary>
public sealed class StockConcurrencyIntegrationTests : IAsyncLifetime
{
    private static readonly TimeSpan BarrierTimeout = TimeSpan.FromSeconds(2);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("INVENTORY_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("e3b1a9f6-2c8d-4a17-9b54-7f2e6c1d8a90")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Set INVENTORY_DB_CONNECTION or the Inventory user-secrets to run this test."
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private InventoryDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<InventoryDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.StockMovements.IgnoreQueryFilters().Where(m => m.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.StockLevels.IgnoreQueryFilters().Where(s => s.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task AdjustAndInvoiceSale_InParallel_KeepBothMovements()
    {
        var itemId = await SeedLevelAsync(10);
        var barrier = new Barrier(2);

        await Task.WhenAll(
            RunAsync(barrier, ReadsLevel, (stock, db) => Adjust(stock, db, itemId, StockMovementType.Purchase, 5)),
            RunAsync(barrier, ReadsLevel, (stock, db) => Commit(stock, db, Guid.NewGuid(), itemId, 3))
        );

        Assert.Equal(12, await QuantityAsync(itemId));
        await AssertChainedLedgerAsync(itemId, from: 10, to: 12);
    }

    [Fact]
    public async Task TwoInvoices_ForLastUnits_OnlyOneSucceeds()
    {
        var itemId = await SeedLevelAsync(3);
        var barrier = new Barrier(2);

        var results = await Task.WhenAll(
            RunAsync(barrier, ReadsLevel, (stock, db) => Commit(stock, db, Guid.NewGuid(), itemId, 3)),
            RunAsync(barrier, ReadsLevel, (stock, db) => Commit(stock, db, Guid.NewGuid(), itemId, 3))
        );

        Assert.Single(results, r => r.IsSuccess);
        Assert.Single(results, r => r.IsFailure && r.Error.Code == InventoryErrors.InsufficientStock.Code);
        Assert.Equal(0, await QuantityAsync(itemId));
        Assert.Single(await MovementsAsync(itemId));
    }

    [Fact]
    public async Task FirstAdjust_OfNewItem_InParallel_CreatesOneLevel()
    {
        var itemId = Guid.NewGuid();
        var barrier = new Barrier(2);

        await Task.WhenAll(
            RunCatchingAsync(barrier, ReadsLevel, (stock, db) => Adjust(stock, db, itemId, StockMovementType.Purchase, 2)),
            RunCatchingAsync(barrier, ReadsLevel, (stock, db) => Adjust(stock, db, itemId, StockMovementType.Purchase, 3))
        );

        await using var db = NewContext();
        var level = Assert.Single(
            await db.StockLevels.IgnoreQueryFilters().Where(s => s.TenantId == _tenantId).ToListAsync()
        );
        Assert.Equal(5, level.QuantityOnHand);
        Assert.Equal(2, (await MovementsAsync(itemId)).Count);
    }

    [Fact]
    public async Task SameInvoice_CommittedTwiceInParallel_DiscountsOnce()
    {
        var itemId = await SeedLevelAsync(10);
        var invoiceId = Guid.NewGuid();
        var barrier = new Barrier(2);

        await Task.WhenAll(
            RunCatchingAsync(barrier, ReadsMovements, (stock, db) => Commit(stock, db, invoiceId, itemId, 4)),
            RunCatchingAsync(barrier, ReadsMovements, (stock, db) => Commit(stock, db, invoiceId, itemId, 4))
        );

        Assert.Equal(6, await QuantityAsync(itemId));
        var movements = await MovementsAsync(itemId);
        var netSold = movements
            .Where(m => m.Reference == invoiceId.ToString())
            .Sum(m =>
                m.Type == StockMovementType.Sale ? m.Quantity
                : m.Type == StockMovementType.Return ? -m.Quantity
                : 0
            );
        Assert.Equal(4, netSold);
    }

    [Fact]
    public async Task ForUpdate_WithoutTransaction_Throws()
    {
        // Sin transacción el candado se soltaría al terminar el SELECT: falla a la vista en vez de no proteger.
        await using var db = NewContext();
        var stock = new StockRepository(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            stock.GetByCatalogItemForUpdateAsync(_tenantId, Guid.NewGuid())
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            stock.AcquireInvoiceSaleLockAsync(_tenantId, Guid.NewGuid())
        );
    }

    // ───────── helpers ─────────

    private static readonly string[] ReadsLevel =
    [
        nameof(IStockRepository.GetByCatalogItemAsync),
        nameof(IStockRepository.GetByCatalogItemsAsync),
        nameof(IStockRepository.GetByCatalogItemForUpdateAsync),
        nameof(IStockRepository.GetByCatalogItemsForUpdateAsync),
    ];

    private static readonly string[] ReadsMovements = [nameof(IStockRepository.GetMovementsByReferenceAsync)];

    private Task<Result> Adjust(
        IStockRepository stock,
        InventoryDbContext db,
        Guid itemId,
        StockMovementType type,
        int quantity
    ) =>
        Unwrap(
            AdjustStockHandler.Handle(
                new AdjustStockCommand(_tenantId, Guid.NewGuid(), itemId, type, quantity, null, "test"),
                stock,
                db,
                CancellationToken.None
            )
        );

    private static async Task<Result> Unwrap<T>(Task<Result<T>> task)
    {
        var result = await task;
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    private Task<Result> Commit(
        IStockRepository stock,
        InventoryDbContext db,
        Guid invoiceId,
        Guid itemId,
        int quantity
    ) =>
        CommitInvoiceSaleHandler.Handle(
            new CommitInvoiceSaleCommand(_tenantId, invoiceId, [new CommitSaleLine(itemId, quantity)]),
            stock,
            db,
            CancellationToken.None
        );

    /// <summary>Una operación con su contexto y su transacción, como la que abre Wolverine.</summary>
    private async Task<Result> RunAsync(
        Barrier barrier,
        string[] waitAfter,
        Func<IStockRepository, InventoryDbContext, Task<Result>> operation
    )
    {
        await using var db = NewContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var stock = new BarrierStockRepository(new StockRepository(db), barrier, waitAfter);
        var result = await operation(stock, db);
        if (result.IsSuccess)
            await transaction.CommitAsync();
        else
            await transaction.RollbackAsync();
        return result;
    }

    // El perdedor de una carrera puede chocar al guardar: cuenta como operación fallida.
    private async Task RunCatchingAsync(
        Barrier barrier,
        string[] waitAfter,
        Func<IStockRepository, InventoryDbContext, Task<Result>> operation
    )
    {
        try
        {
            await RunAsync(barrier, waitAfter, operation);
        }
        catch (Exception ex) when (ex is DbUpdateException or ConflictException) { }
    }

    private async Task<Guid> SeedLevelAsync(int quantity)
    {
        await using var db = NewContext();
        var level = StockLevel.Create(_tenantId, Guid.NewGuid(), quantity, 0, 0, 0, DateTime.UtcNow).Value;
        db.StockLevels.Add(level);
        await db.SaveChangesAsync();
        return level.CatalogItemId;
    }

    private async Task<int> QuantityAsync(Guid itemId)
    {
        await using var db = NewContext();
        return await db
            .StockLevels.IgnoreQueryFilters()
            .Where(s => s.TenantId == _tenantId && s.CatalogItemId == itemId)
            .Select(s => s.QuantityOnHand)
            .SingleAsync();
    }

    private async Task<List<StockMovement>> MovementsAsync(Guid itemId)
    {
        await using var db = NewContext();
        return await db
            .StockMovements.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.TenantId == _tenantId && m.CatalogItemId == itemId)
            .ToListAsync();
    }

    /// <summary>Cada movimiento parte de donde quedó el anterior: ninguna lectura vieja.</summary>
    private async Task AssertChainedLedgerAsync(Guid itemId, int from, int to)
    {
        var pending = await MovementsAsync(itemId);
        var current = from;
        while (pending.Count > 0)
        {
            var next = pending.FirstOrDefault(m => m.PreviousQuantity == current);
            Assert.True(next is not null, $"No movement starts at {current}: the ledger chain is broken.");
            pending.Remove(next!);
            current = next!.NewQuantity;
        }
        Assert.Equal(to, current);
    }

    /// <summary>Espera en la barrera después de las lecturas indicadas, para que las dos operaciones lean antes
    /// de que alguna guarde. Con candado, la segunda queda bloqueada en la lectura y la barrera vence sola.</summary>
    private sealed class BarrierStockRepository(IStockRepository inner, Barrier barrier, string[] waitAfter)
        : IStockRepository
    {
        private async Task<T> Wait<T>(string method, Task<T> read)
        {
            var value = await read;
            if (waitAfter.Contains(method))
                await Task.Run(() => barrier.SignalAndWait(BarrierTimeout));
            return value;
        }

        public Task<StockLevel?> GetByCatalogItemAsync(Guid tenantId, Guid catalogItemId, CancellationToken ct = default) =>
            Wait(nameof(GetByCatalogItemAsync), inner.GetByCatalogItemAsync(tenantId, catalogItemId, ct));

        public Task<IReadOnlyList<StockLevel>> GetByCatalogItemsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> catalogItemIds,
            CancellationToken ct = default
        ) => Wait(nameof(GetByCatalogItemsAsync), inner.GetByCatalogItemsAsync(tenantId, catalogItemIds, ct));

        public Task<StockLevel?> GetByCatalogItemForUpdateAsync(
            Guid tenantId,
            Guid catalogItemId,
            CancellationToken ct = default
        ) => Wait(nameof(GetByCatalogItemForUpdateAsync), inner.GetByCatalogItemForUpdateAsync(tenantId, catalogItemId, ct));

        public Task<IReadOnlyList<StockLevel>> GetByCatalogItemsForUpdateAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> catalogItemIds,
            CancellationToken ct = default
        ) =>
            Wait(
                nameof(GetByCatalogItemsForUpdateAsync),
                inner.GetByCatalogItemsForUpdateAsync(tenantId, catalogItemIds, ct)
            );

        public Task AcquireInvoiceSaleLockAsync(Guid tenantId, Guid invoiceId, CancellationToken ct = default) =>
            inner.AcquireInvoiceSaleLockAsync(tenantId, invoiceId, ct);

        public Task<IReadOnlyList<StockMovement>> GetMovementsByReferenceAsync(
            Guid tenantId,
            string reference,
            CancellationToken ct = default
        ) => Wait(nameof(GetMovementsByReferenceAsync), inner.GetMovementsByReferenceAsync(tenantId, reference, ct));

        public Task AddStockLevelAsync(StockLevel level, CancellationToken ct = default) =>
            inner.AddStockLevelAsync(level, ct);

        public Task AddMovementAsync(StockMovement movement, CancellationToken ct = default) =>
            inner.AddMovementAsync(movement, ct);

        public Task<(IReadOnlyList<StockLevel> Items, int Total)> ListStockLevelsAsync(
            Guid tenantId,
            bool lowStockOnly,
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => inner.ListStockLevelsAsync(tenantId, lowStockOnly, page, pageSize, ct);

        public Task<(IReadOnlyList<StockMovement> Items, int Total)> ListMovementsAsync(
            Guid tenantId,
            Guid? catalogItemId,
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => inner.ListMovementsAsync(tenantId, catalogItemId, page, pageSize, ct);
    }
}
