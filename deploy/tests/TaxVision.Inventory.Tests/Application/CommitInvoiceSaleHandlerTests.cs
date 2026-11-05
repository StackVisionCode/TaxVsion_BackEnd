using TaxVision.Inventory.Application.Stock;
using TaxVision.Inventory.Domain;
using TaxVision.Inventory.Domain.Stock;
using TaxVision.Inventory.Tests.Fakes;

namespace TaxVision.Inventory.Tests.Application;

/// <summary>Venta de una factura reconciliada por delta (emitir, editar, anular). Red de regresión del handler
/// y orden de los candados (INV-1/INV-2).</summary>
public sealed class CommitInvoiceSaleHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InvoiceId = Guid.NewGuid();
    private readonly FakeStockRepository _stock = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Issue_discounts_full_quantity()
    {
        var level = Seed(10);

        var result = await CommitAsync((level.CatalogItemId, 3));

        Assert.True(result.IsSuccess);
        Assert.Equal(7, level.QuantityOnHand);
        var sale = Assert.Single(_stock.Movements);
        Assert.Equal(StockMovementType.Sale, sale.Type);
        Assert.Equal(3, sale.Quantity);
        Assert.Equal(InvoiceId.ToString(), sale.Reference);
    }

    [Fact]
    public async Task Edit_applies_only_delta()
    {
        var level = Seed(10);
        await CommitAsync((level.CatalogItemId, 3));

        await CommitAsync((level.CatalogItemId, 5));
        Assert.Equal(5, level.QuantityOnHand);
        Assert.Equal((StockMovementType.Sale, 2), (_stock.Movements[1].Type, _stock.Movements[1].Quantity));

        await CommitAsync((level.CatalogItemId, 1));
        Assert.Equal(9, level.QuantityOnHand);
        Assert.Equal((StockMovementType.Return, 4), (_stock.Movements[2].Type, _stock.Movements[2].Quantity));
    }

    [Fact]
    public async Task Void_returns_everything()
    {
        var level = Seed(10);
        await CommitAsync((level.CatalogItemId, 4));

        var result = await CommitAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(10, level.QuantityOnHand);
        Assert.Equal((StockMovementType.Return, 4), (_stock.Movements[^1].Type, _stock.Movements[^1].Quantity));
    }

    [Fact]
    public async Task Reissue_same_desired_state_moves_nothing()
    {
        var level = Seed(10);
        await CommitAsync((level.CatalogItemId, 3));
        var saves = _unitOfWork.SaveChangesCallCount;

        var result = await CommitAsync((level.CatalogItemId, 3));

        Assert.True(result.IsSuccess);
        Assert.Single(_stock.Movements);
        Assert.Equal(7, level.QuantityOnHand);
        Assert.Equal(saves, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Shortfall_blocks_without_moving()
    {
        var enough = Seed(10);
        var scarce = Seed(1);

        var result = await CommitAsync((enough.CatalogItemId, 2), (scarce.CatalogItemId, 3));

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.InsufficientStock.Code, result.Error.Code);
        Assert.Empty(_stock.Movements);
        Assert.Equal(10, enough.QuantityOnHand);
        Assert.Equal(1, scarce.QuantityOnHand);
    }

    [Fact]
    public async Task Untracked_or_missing_level_is_ignored()
    {
        var untracked = Seed(0);
        untracked.SetTracked(false, DateTime.UtcNow);

        var result = await CommitAsync((untracked.CatalogItemId, 5), (Guid.NewGuid(), 2));

        Assert.True(result.IsSuccess);
        Assert.Empty(_stock.Movements);
        Assert.Equal(0, untracked.QuantityOnHand);
    }

    [Fact]
    public async Task Acquires_invoice_lock_before_reading_movements_and_levels()
    {
        // Candado de la factura → leer el ledger → leer niveles con candado (INV-2 + INV-1).
        var level = Seed(10);

        await CommitAsync((level.CatalogItemId, 3));

        Assert.Equal(
            new[]
            {
                nameof(FakeStockRepository.AcquireInvoiceSaleLockAsync),
                nameof(FakeStockRepository.GetMovementsByReferenceAsync),
                nameof(FakeStockRepository.GetByCatalogItemsForUpdateAsync),
            },
            _stock.Calls
        );
    }

    private StockLevel Seed(int quantity)
    {
        var level = StockLevel.Create(TenantId, Guid.NewGuid(), quantity, 0, 0, 0, DateTime.UtcNow).Value;
        _stock.Seed(level);
        return level;
    }

    private Task<BuildingBlocks.Results.Result> CommitAsync(params (Guid ItemId, int Quantity)[] lines) =>
        CommitInvoiceSaleHandler.Handle(
            new CommitInvoiceSaleCommand(
                TenantId,
                InvoiceId,
                lines.Select(l => new CommitSaleLine(l.ItemId, l.Quantity)).ToList()
            ),
            _stock,
            _unitOfWork,
            CancellationToken.None
        );
}
