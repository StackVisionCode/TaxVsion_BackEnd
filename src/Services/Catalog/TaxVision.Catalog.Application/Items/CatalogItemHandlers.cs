using BuildingBlocks.Messaging.CatalogIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Catalog.Application.Abstractions;
using TaxVision.Catalog.Application.Common;
using TaxVision.Catalog.Domain;
using TaxVision.Catalog.Domain.Items;
using TaxVision.Catalog.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Catalog.Application.Items;

// ───────────────────────── Commands ─────────────────────────

public sealed record CreateCatalogItemCommand(
    Guid TenantId,
    Guid TaxUserId,
    string Name,
    string? Description,
    string? Sku,
    string? Barcode,
    Guid CategoryId,
    ItemKind Kind,
    decimal PriceAmount,
    string PriceCurrency,
    decimal? CostAmount,
    string? CostCurrency,
    string? Unit,
    bool TrackInventory,
    string? ImageUrl,
    IReadOnlyList<CatalogItemAttributeDto>? Attributes
);

public sealed record UpdateCatalogItemCommand(
    Guid TenantId,
    Guid Id,
    string Name,
    string? Description,
    string? Barcode,
    Guid CategoryId,
    string? Unit,
    string? ImageUrl,
    IReadOnlyList<CatalogItemAttributeDto>? Attributes
);

public sealed record ChangeCatalogItemPriceCommand(
    Guid TenantId,
    Guid Id,
    decimal PriceAmount,
    string PriceCurrency,
    decimal? CostAmount,
    string? CostCurrency
);

public sealed record SetCatalogItemActiveCommand(Guid TenantId, Guid Id, bool IsActive);

public sealed record DeleteCatalogItemCommand(Guid TenantId, Guid Id);

public static class CreateCatalogItemHandler
{
    public static async Task<Result<CatalogItemDto>> Handle(
        CreateCatalogItemCommand command,
        ICatalogItemRepository items,
        ICategoryRepository categories,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!await categories.ExistsAsync(command.TenantId, command.CategoryId, ct))
            return Result.Failure<CatalogItemDto>(CatalogErrors.CategoryNotFound);

        var money = BuildMoney(command.PriceAmount, command.PriceCurrency, command.CostAmount, command.CostCurrency);
        if (money.IsFailure)
            return Result.Failure<CatalogItemDto>(money.Error);
        var (price, cost) = money.Value;

        var normalizedSku = string.IsNullOrWhiteSpace(command.Sku) ? null : command.Sku.Trim().ToUpperInvariant();
        if (normalizedSku is not null && await items.SkuExistsAsync(command.TenantId, normalizedSku, null, ct))
            return Result.Failure<CatalogItemDto>(CatalogErrors.DuplicateSku);

        var created = CatalogItem.Create(
            command.TenantId,
            command.TaxUserId,
            command.Name,
            command.Description,
            command.Sku,
            command.Barcode,
            command.CategoryId,
            command.Kind,
            price,
            cost,
            command.Unit,
            command.TrackInventory,
            command.ImageUrl,
            DateTime.UtcNow
        );
        if (created.IsFailure)
            return Result.Failure<CatalogItemDto>(created.Error);

        var item = created.Value;
        if (command.Attributes is { Count: > 0 })
            item.ReplaceAttributes(command.Attributes.Select(a => (a.Key, a.Value, a.ValueType)));

        await items.AddAsync(item, ct);
        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new CatalogItemCreatedIntegrationEvent
            {
                TenantId = command.TenantId,
                ItemId = item.Id,
                CategoryId = item.CategoryId,
                Name = item.Name,
                Sku = item.Sku,
                Kind = item.Kind.ToString(),
                TrackInventory = item.TrackInventory,
                UnitPrice = item.Price.Amount,
                Currency = item.Price.Currency,
            }
        );
        return Result.Success(item.ToDto());
    }

    internal static Result<(Money Price, Money? Cost)> BuildMoney(
        decimal priceAmount,
        string priceCurrency,
        decimal? costAmount,
        string? costCurrency
    )
    {
        var priceResult = Money.Create(priceAmount, priceCurrency);
        if (priceResult.IsFailure)
            return Result.Failure<(Money, Money?)>(priceResult.Error);

        if (costAmount is not { } amount)
            return Result.Success<(Money, Money?)>((priceResult.Value, null));

        var costResult = Money.Create(amount, costCurrency ?? priceCurrency);
        if (costResult.IsFailure)
            return Result.Failure<(Money, Money?)>(costResult.Error);

        return Result.Success<(Money, Money?)>((priceResult.Value, costResult.Value));
    }
}

public static class UpdateCatalogItemHandler
{
    public static async Task<Result<CatalogItemDto>> Handle(
        UpdateCatalogItemCommand command,
        ICatalogItemRepository items,
        ICategoryRepository categories,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var item = await items.GetByIdAsync(command.TenantId, command.Id, ct);
        if (item is null)
            return Result.Failure<CatalogItemDto>(CatalogErrors.ItemNotFound);
        if (!await categories.ExistsAsync(command.TenantId, command.CategoryId, ct))
            return Result.Failure<CatalogItemDto>(CatalogErrors.CategoryNotFound);

        var updated = item.Update(
            command.Name,
            command.Description,
            command.Barcode,
            command.CategoryId,
            command.Unit,
            command.ImageUrl,
            DateTime.UtcNow
        );
        if (updated.IsFailure)
            return Result.Failure<CatalogItemDto>(updated.Error);

        if (command.Attributes is not null)
            item.ReplaceAttributes(command.Attributes.Select(a => (a.Key, a.Value, a.ValueType)));

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new CatalogItemUpdatedIntegrationEvent
            {
                TenantId = command.TenantId,
                ItemId = item.Id,
                Name = item.Name,
                CategoryId = item.CategoryId,
            }
        );
        return Result.Success(item.ToDto());
    }
}

public static class ChangeCatalogItemPriceHandler
{
    public static async Task<Result<CatalogItemDto>> Handle(
        ChangeCatalogItemPriceCommand command,
        ICatalogItemRepository items,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var item = await items.GetByIdAsync(command.TenantId, command.Id, ct);
        if (item is null)
            return Result.Failure<CatalogItemDto>(CatalogErrors.ItemNotFound);

        var money = CreateCatalogItemHandler.BuildMoney(
            command.PriceAmount,
            command.PriceCurrency,
            command.CostAmount,
            command.CostCurrency
        );
        if (money.IsFailure)
            return Result.Failure<CatalogItemDto>(money.Error);

        item.ChangePrice(money.Value.Price, money.Value.Cost, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new CatalogItemPriceChangedIntegrationEvent
            {
                TenantId = command.TenantId,
                ItemId = item.Id,
                UnitPrice = item.Price.Amount,
                Currency = item.Price.Currency,
            }
        );
        return Result.Success(item.ToDto());
    }
}

public static class SetCatalogItemActiveHandler
{
    public static async Task<Result> Handle(
        SetCatalogItemActiveCommand command,
        ICatalogItemRepository items,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var item = await items.GetByIdAsync(command.TenantId, command.Id, ct);
        if (item is null)
            return Result.Failure(CatalogErrors.ItemNotFound);

        item.SetActive(command.IsActive, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        if (!command.IsActive)
            await bus.PublishAsync(
                new CatalogItemDeactivatedIntegrationEvent { TenantId = command.TenantId, ItemId = item.Id }
            );
        return Result.Success();
    }
}

public static class DeleteCatalogItemHandler
{
    public static async Task<Result> Handle(
        DeleteCatalogItemCommand command,
        ICatalogItemRepository items,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var item = await items.GetByIdAsync(command.TenantId, command.Id, ct);
        if (item is null)
            return Result.Failure(CatalogErrors.ItemNotFound);

        item.SoftDelete(DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        // Borrado (soft) = desactivado para los consumidores (Inventory/Billing).
        await bus.PublishAsync(
            new CatalogItemDeactivatedIntegrationEvent { TenantId = command.TenantId, ItemId = item.Id }
        );
        return Result.Success();
    }
}

// ───────────────────────── Queries ─────────────────────────

public sealed record GetCatalogItemQuery(Guid TenantId, Guid Id);

public sealed record ListCatalogItemsQuery(
    Guid TenantId,
    Guid? CategoryId,
    string? Search,
    bool ActiveOnly,
    int Page,
    int PageSize
);

public static class GetCatalogItemHandler
{
    public static async Task<Result<CatalogItemDto>> Handle(
        GetCatalogItemQuery query,
        ICatalogItemRepository items,
        CancellationToken ct
    )
    {
        var item = await items.GetByIdAsync(query.TenantId, query.Id, ct);
        return item is null ? Result.Failure<CatalogItemDto>(CatalogErrors.ItemNotFound) : Result.Success(item.ToDto());
    }
}

public static class ListCatalogItemsHandler
{
    public static async Task<Result<PagedResult<CatalogItemDto>>> Handle(
        ListCatalogItemsQuery query,
        ICatalogItemRepository items,
        CancellationToken ct
    )
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 200 ? 50 : query.PageSize;

        var (rows, total) = await items.ListAsync(
            query.TenantId,
            query.CategoryId,
            query.Search,
            query.ActiveOnly,
            page,
            pageSize,
            ct
        );
        var dtos = rows.Select(i => i.ToDto()).ToList();
        return Result.Success(new PagedResult<CatalogItemDto>(dtos, total, page, pageSize));
    }
}
