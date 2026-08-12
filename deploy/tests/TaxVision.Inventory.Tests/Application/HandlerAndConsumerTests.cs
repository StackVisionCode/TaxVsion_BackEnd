using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CatalogIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Inventory.Application.Consumers;
using TaxVision.Inventory.Application.Permissions.Consumers;
using TaxVision.Inventory.Application.Stock;
using TaxVision.Inventory.Application.Suppliers;
using TaxVision.Inventory.Domain;
using TaxVision.Inventory.Domain.Stock;
using TaxVision.Inventory.Tests.Fakes;

namespace TaxVision.Inventory.Tests.Application;

public sealed class StockHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Item = Guid.NewGuid();

    [Fact]
    public async Task Adjust_auto_creates_level_and_writes_ledger()
    {
        var stock = new FakeStockRepository();
        var uow = new FakeUnitOfWork();
        var result = await AdjustStockHandler.Handle(
            new AdjustStockCommand(Tenant, User, Item, StockMovementType.Purchase, 10, "PO-1", null), stock, uow, CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value.QuantityOnHand);
        Assert.Single(stock.Levels);
        var move = Assert.Single(stock.Movements);
        Assert.Equal(0, move.PreviousQuantity);
        Assert.Equal(10, move.NewQuantity);
        Assert.Equal("PO-1", move.Reference);
    }

    [Fact]
    public async Task Adjust_sale_beyond_stock_fails_and_writes_no_ledger()
    {
        var stock = new FakeStockRepository();
        stock.Seed(StockLevel.Create(Tenant, Item, 2, 0, 0, 0, DateTime.UtcNow).Value);
        var result = await AdjustStockHandler.Handle(
            new AdjustStockCommand(Tenant, User, Item, StockMovementType.Sale, 5, null, null), stock, new FakeUnitOfWork(), CancellationToken.None
        );
        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.InsufficientStock.Code, result.Error.Code);
        Assert.Empty(stock.Movements);
    }

    [Fact]
    public async Task Adjust_zero_quantity_fails()
    {
        var result = await AdjustStockHandler.Handle(
            new AdjustStockCommand(Tenant, User, Item, StockMovementType.Purchase, 0, null, null), new FakeStockRepository(), new FakeUnitOfWork(), CancellationToken.None
        );
        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.InvalidQuantity.Code, result.Error.Code);
    }

    [Fact]
    public async Task SetThresholds_creates_or_updates_level()
    {
        var stock = new FakeStockRepository();
        var result = await SetStockThresholdsHandler.Handle(
            new SetStockThresholdsCommand(Tenant, Item, 5, 100, 10), stock, new FakeUnitOfWork(), CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.MinLevel);
        Assert.Single(stock.Levels);
    }
}

public sealed class SupplierHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public async Task Create_supplier_persists()
    {
        var repo = new FakeSupplierRepository();
        var result = await CreateSupplierHandler.Handle(
            new CreateSupplierCommand(Tenant, User, "ACME", null, null, null, null, null), repo, new FakeUnitOfWork(), CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        Assert.Single(repo.Store);
    }

    [Fact]
    public async Task Upsert_item_supplier_requires_existing_supplier()
    {
        var links = new FakeItemSupplierRepository();
        var suppliers = new FakeSupplierRepository();
        var result = await UpsertItemSupplierHandler.Handle(
            new UpsertItemSupplierCommand(Tenant, Guid.NewGuid(), Guid.NewGuid(), null, 10, "USD", null, false), links, suppliers, new FakeUnitOfWork(), CancellationToken.None
        );
        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.SupplierNotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task Upsert_item_supplier_creates_then_updates()
    {
        var links = new FakeItemSupplierRepository();
        var suppliers = new FakeSupplierRepository();
        var supplier = TaxVision.Inventory.Domain.Suppliers.Supplier.Create(Tenant, User, "ACME", null, null, null, null, null, DateTime.UtcNow).Value;
        suppliers.Seed(supplier);
        var item = Guid.NewGuid();

        var created = await UpsertItemSupplierHandler.Handle(
            new UpsertItemSupplierCommand(Tenant, item, supplier.Id, "SKU-A", 50, "USD", 7, true), links, suppliers, new FakeUnitOfWork(), CancellationToken.None
        );
        Assert.True(created.IsSuccess);
        Assert.Single(links.Store);

        var updated = await UpsertItemSupplierHandler.Handle(
            new UpsertItemSupplierCommand(Tenant, item, supplier.Id, "SKU-B", 60, "USD", 5, false), links, suppliers, new FakeUnitOfWork(), CancellationToken.None
        );
        Assert.True(updated.IsSuccess);
        Assert.Single(links.Store); // still one (upserted)
        Assert.Equal("SKU-B", links.Store[0].SupplierSku);
    }
}

public sealed class CatalogItemConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task CatalogItemCreated_opens_stock_level_for_trackable_product()
    {
        var stock = new FakeStockRepository();
        var evt = new CatalogItemCreatedIntegrationEvent
        {
            TenantId = Tenant, ItemId = Guid.NewGuid(), CategoryId = Guid.NewGuid(), Name = "Widget",
            Kind = "Product", TrackInventory = true, UnitPrice = 100, Currency = "USD",
        };
        await CatalogItemCreatedConsumer.Handle(evt, stock, new FakeUnitOfWork(), new FakeCorrelationContext(), NullLogger<StockLevel>.Instance, CancellationToken.None);
        Assert.Single(stock.Levels);
        Assert.Equal(evt.ItemId, stock.Levels[0].CatalogItemId);
    }

    [Fact]
    public async Task CatalogItemCreated_skips_non_trackable()
    {
        var stock = new FakeStockRepository();
        var evt = new CatalogItemCreatedIntegrationEvent
        {
            TenantId = Tenant, ItemId = Guid.NewGuid(), CategoryId = Guid.NewGuid(), Name = "Service",
            Kind = "Service", TrackInventory = false, UnitPrice = 100, Currency = "USD",
        };
        await CatalogItemCreatedConsumer.Handle(evt, stock, new FakeUnitOfWork(), new FakeCorrelationContext(), NullLogger<StockLevel>.Instance, CancellationToken.None);
        Assert.Empty(stock.Levels);
    }

    [Fact]
    public async Task CatalogItemDeactivated_untracks_existing_level()
    {
        var stock = new FakeStockRepository();
        var item = Guid.NewGuid();
        stock.Seed(StockLevel.Create(Tenant, item, 5, 0, 0, 0, DateTime.UtcNow).Value);
        await CatalogItemDeactivatedConsumer.Handle(
            new CatalogItemDeactivatedIntegrationEvent { TenantId = Tenant, ItemId = item }, stock, new FakeUnitOfWork(), new FakeCorrelationContext(), NullLogger<StockLevel>.Instance, CancellationToken.None
        );
        Assert.False(stock.Levels[0].IsTracked);
    }
}

public sealed class PermissionsProjectionConsumerTests
{
    [Fact]
    public async Task UserRolesChanged_creates_projection()
    {
        var users = new FakeUserPermissionsProjectionRepository();
        var evt = new UserRolesChangedIntegrationEvent
        {
            TenantId = Guid.NewGuid(), UserId = Guid.NewGuid(), PermissionsVersion = 2,
            PermissionCodes = ["inventory.read", "inventory.adjust"], RoleIds = [Guid.NewGuid()], ActorType = "TenantAdmin",
        };
        await UserRolesChangedPermissionsProjectionConsumer.Handle(evt, users, new FakeUnitOfWork(), new FakeCorrelationContext(), NullLogger<TaxVision.Inventory.Domain.Permissions.UserPermissionsProjection>.Instance, CancellationToken.None);
        var stored = await users.GetAsync(evt.TenantId, evt.UserId);
        Assert.Contains("inventory.adjust", stored!.PermissionCodes());
    }
}
