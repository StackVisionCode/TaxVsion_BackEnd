using BuildingBlocks.Messaging.CatalogIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Catalog.Application.Common;
using TaxVision.Catalog.Application.Items;
using TaxVision.Catalog.Domain;
using TaxVision.Catalog.Domain.Categories;
using TaxVision.Catalog.Domain.Items;
using TaxVision.Catalog.Domain.ValueObjects;
using TaxVision.Catalog.Tests.Fakes;

namespace TaxVision.Catalog.Tests.Application;

public sealed class CatalogItemHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc);

    private sealed class Harness
    {
        public FakeCatalogItemRepository Items { get; } = new();
        public FakeCategoryRepository Categories { get; } = new();
        public FakeUnitOfWork Uow { get; } = new();
        public FakeMessageBus Bus { get; } = new();
        public Guid CategoryId { get; }

        public Harness()
        {
            var cat = Category.Create(Tenant, User, "Cat", null, null, Now).Value;
            Categories.Seed(cat);
            CategoryId = cat.Id;
        }

        public CatalogItem SeedItem(string sku = "SKU-1")
        {
            var item = CatalogItem
                .Create(
                    Tenant,
                    User,
                    "Widget",
                    null,
                    sku,
                    null,
                    CategoryId,
                    ItemKind.Product,
                    Money.Create(100, "USD").Value,
                    null,
                    null,
                    true,
                    null,
                    Now
                )
                .Value;
            Items.Seed(item);
            return item;
        }
    }

    private static CreateCatalogItemCommand CreateCmd(Guid categoryId, string? sku = "SKU-1") =>
        new(
            Tenant,
            User,
            "Widget",
            null,
            sku,
            null,
            categoryId,
            ItemKind.Product,
            100,
            "USD",
            null,
            null,
            null,
            true,
            null,
            null
        );

    [Fact]
    public async Task Create_fails_when_category_missing()
    {
        var h = new Harness();
        var result = await CreateCatalogItemHandler.Handle(
            CreateCmd(Guid.NewGuid()),
            h.Items,
            h.Categories,
            h.Uow,
            h.Bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );
        Assert.True(result.IsFailure);
        Assert.Equal(CatalogErrors.CategoryNotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task Create_persists_and_publishes_created_event()
    {
        var h = new Harness();
        var result = await CreateCatalogItemHandler.Handle(
            CreateCmd(h.CategoryId),
            h.Items,
            h.Categories,
            h.Uow,
            h.Bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("USD", result.Value.Price.Currency);
        Assert.Single(h.Items.Added);
        Assert.Equal(1, h.Uow.SaveChangesCallCount);
        Assert.NotNull(h.Bus.LastOfType<CatalogItemCreatedIntegrationEvent>());
    }

    [Fact]
    public async Task Create_duplicate_sku_fails()
    {
        var h = new Harness();
        h.SeedItem("DUP-1");
        var result = await CreateCatalogItemHandler.Handle(
            CreateCmd(h.CategoryId, "dup-1"),
            h.Items,
            h.Categories,
            h.Uow,
            h.Bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );
        Assert.True(result.IsFailure);
        Assert.Equal(CatalogErrors.DuplicateSku.Code, result.Error.Code);
    }

    [Fact]
    public async Task ChangePrice_not_found_fails()
    {
        var h = new Harness();
        var result = await ChangeCatalogItemPriceHandler.Handle(
            new ChangeCatalogItemPriceCommand(Tenant, Guid.NewGuid(), 200, "DOP", null, null),
            h.Items,
            h.Uow,
            h.Bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );
        Assert.True(result.IsFailure);
        Assert.Equal(CatalogErrors.ItemNotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task ChangePrice_success_publishes_pricechanged()
    {
        var h = new Harness();
        var item = h.SeedItem();
        var result = await ChangeCatalogItemPriceHandler.Handle(
            new ChangeCatalogItemPriceCommand(Tenant, item.Id, 250, "DOP", null, null),
            h.Items,
            h.Uow,
            h.Bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        Assert.Equal(250, result.Value.Price.Amount);
        Assert.Equal("DOP", result.Value.Price.Currency);
        Assert.NotNull(h.Bus.LastOfType<CatalogItemPriceChangedIntegrationEvent>());
    }

    [Fact]
    public async Task Update_success_publishes_updated()
    {
        var h = new Harness();
        var item = h.SeedItem();
        var result = await UpdateCatalogItemHandler.Handle(
            new UpdateCatalogItemCommand(Tenant, item.Id, "New Name", null, null, h.CategoryId, null, null, null),
            h.Items,
            h.Categories,
            h.Uow,
            h.Bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", result.Value.Name);
        Assert.NotNull(h.Bus.LastOfType<CatalogItemUpdatedIntegrationEvent>());
    }

    [Fact]
    public async Task SetActive_false_publishes_deactivated_true_does_not()
    {
        var h = new Harness();
        var item = h.SeedItem();

        await SetCatalogItemActiveHandler.Handle(
            new SetCatalogItemActiveCommand(Tenant, item.Id, false),
            h.Items,
            h.Uow,
            h.Bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );
        Assert.NotNull(h.Bus.LastOfType<CatalogItemDeactivatedIntegrationEvent>());

        var bus2 = new FakeMessageBus();
        await SetCatalogItemActiveHandler.Handle(
            new SetCatalogItemActiveCommand(Tenant, item.Id, true),
            h.Items,
            h.Uow,
            bus2,
            new FakeCorrelationContext(),
            CancellationToken.None
        );
        Assert.Null(bus2.LastOfType<CatalogItemDeactivatedIntegrationEvent>());
    }

    [Fact]
    public async Task Delete_soft_deletes_and_publishes_deactivated()
    {
        var h = new Harness();
        var item = h.SeedItem();
        var result = await DeleteCatalogItemHandler.Handle(
            new DeleteCatalogItemCommand(Tenant, item.Id),
            h.Items,
            h.Uow,
            h.Bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        Assert.True(item.IsDeleted);
        Assert.NotNull(h.Bus.LastOfType<CatalogItemDeactivatedIntegrationEvent>());
    }

    [Fact]
    public async Task Get_returns_item_or_not_found()
    {
        var h = new Harness();
        var item = h.SeedItem();
        Assert.True(
            (
                await GetCatalogItemHandler.Handle(
                    new GetCatalogItemQuery(Tenant, item.Id),
                    h.Items,
                    CancellationToken.None
                )
            ).IsSuccess
        );
        Assert.True(
            (
                await GetCatalogItemHandler.Handle(
                    new GetCatalogItemQuery(Tenant, Guid.NewGuid()),
                    h.Items,
                    CancellationToken.None
                )
            ).IsFailure
        );
    }

    [Fact]
    public async Task List_returns_paged_results()
    {
        var h = new Harness();
        h.SeedItem("A-1");
        h.SeedItem("B-1");
        var result = await ListCatalogItemsHandler.Handle(
            new ListCatalogItemsQuery(Tenant, null, null, false, 1, 50),
            h.Items,
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Total);
    }
}
