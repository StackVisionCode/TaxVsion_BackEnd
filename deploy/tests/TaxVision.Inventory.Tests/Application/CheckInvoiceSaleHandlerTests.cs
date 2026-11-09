using TaxVision.Inventory.Application.Stock;
using TaxVision.Inventory.Domain;
using TaxVision.Inventory.Domain.Stock;
using TaxVision.Inventory.Tests.Fakes;

namespace TaxVision.Inventory.Tests.Application;

/// <summary>Verificación de stock de solo lectura antes de emitir o editar una factura (D-BIL-2 (a)).</summary>
public sealed class CheckInvoiceSaleHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InvoiceId = Guid.NewGuid();
    private readonly FakeStockRepository _stock = new();

    [Fact]
    public async Task Shortfall_returns_insufficient_stock()
    {
        var level = Seed(2);

        var result = await CheckAsync((level.CatalogItemId, 3));

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.InsufficientStock.Code, result.Error.Code);
    }

    [Fact]
    public async Task Counts_what_the_invoice_already_discounted()
    {
        // La factura ya descontó 3 y quedan 1: subir a 4 alcanza, subir a 5 no.
        var level = Seed(1);
        SeedSale(level.CatalogItemId, 3);

        Assert.True((await CheckAsync((level.CatalogItemId, 4))).IsSuccess);
        var over = await CheckAsync((level.CatalogItemId, 5));
        Assert.True(over.IsFailure);
        Assert.Equal(InventoryErrors.InsufficientStock.Code, over.Error.Code);
    }

    [Fact]
    public async Task Decrease_never_blocks()
    {
        var level = Seed(0);
        SeedSale(level.CatalogItemId, 3);

        var result = await CheckAsync((level.CatalogItemId, 1));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Untracked_or_missing_level_is_ignored()
    {
        var untracked = Seed(0);
        untracked.SetTracked(false, DateTime.UtcNow);

        var result = await CheckAsync((untracked.CatalogItemId, 5), (Guid.NewGuid(), 2));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Moves_nothing_and_takes_no_lock()
    {
        var level = Seed(10);

        await CheckAsync((level.CatalogItemId, 3));

        Assert.Empty(_stock.Movements);
        Assert.Equal(10, level.QuantityOnHand);
        Assert.DoesNotContain(nameof(FakeStockRepository.AcquireInvoiceSaleLockAsync), _stock.Calls);
        Assert.DoesNotContain(nameof(FakeStockRepository.GetByCatalogItemsForUpdateAsync), _stock.Calls);
    }

    private StockLevel Seed(int quantity)
    {
        var level = StockLevel.Create(TenantId, Guid.NewGuid(), quantity, 0, 0, 0, DateTime.UtcNow).Value;
        _stock.Seed(level);
        return level;
    }

    private void SeedSale(Guid catalogItemId, int quantity) =>
        _stock.Movements.Add(
            new StockMovement(
                TenantId,
                catalogItemId,
                StockMovementType.Sale,
                quantity,
                quantity,
                0,
                InvoiceId.ToString(),
                "Invoice sale",
                Guid.Empty,
                DateTime.UtcNow
            )
        );

    private Task<BuildingBlocks.Results.Result> CheckAsync(params (Guid ItemId, int Quantity)[] lines) =>
        CheckInvoiceSaleHandler.Handle(
            new CheckInvoiceSaleQuery(
                TenantId,
                InvoiceId,
                lines.Select(l => new CommitSaleLine(l.ItemId, l.Quantity)).ToList()
            ),
            _stock,
            CancellationToken.None
        );
}
