using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Inventory.Application.Abstractions;
using TaxVision.Inventory.Application.Common;
using TaxVision.Inventory.Domain;
using TaxVision.Inventory.Domain.Stock;

namespace TaxVision.Inventory.Application.Stock;

// ───────────────────────── Commands ─────────────────────────

public sealed record AdjustStockCommand(
    Guid TenantId,
    Guid UserId,
    Guid CatalogItemId,
    StockMovementType Type,
    int Quantity,
    string? Reference,
    string? Notes
);

public sealed record SetStockThresholdsCommand(
    Guid TenantId,
    Guid CatalogItemId,
    int MinLevel,
    int MaxLevel,
    int ReorderPoint
);

public static class AdjustStockHandler
{
    public static async Task<Result<StockLevelDto>> Handle(
        AdjustStockCommand command,
        IStockRepository stock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (command.CatalogItemId == Guid.Empty)
            return Result.Failure<StockLevelDto>(InventoryErrors.InvalidCatalogItem);
        if (command.Quantity == 0)
            return Result.Failure<StockLevelDto>(InventoryErrors.InvalidQuantity);

        var nowUtc = DateTime.UtcNow;
        var level = await stock.GetByCatalogItemAsync(command.TenantId, command.CatalogItemId, ct);
        if (level is null)
        {
            // Primer movimiento de un ítem sin nivel aún: se crea en 0 y se aplica.
            var created = StockLevel.Create(command.TenantId, command.CatalogItemId, 0, 0, 0, 0, nowUtc);
            if (created.IsFailure)
                return Result.Failure<StockLevelDto>(created.Error);
            level = created.Value;
            await stock.AddStockLevelAsync(level, ct);
        }

        var move = level.RegisterMovement(command.Type, command.Quantity, nowUtc);
        if (move.IsFailure)
            return Result.Failure<StockLevelDto>(move.Error);

        await stock.AddMovementAsync(
            new StockMovement(
                command.TenantId,
                command.CatalogItemId,
                command.Type,
                command.Quantity,
                move.Value.Previous,
                move.Value.New,
                command.Reference,
                command.Notes,
                command.UserId,
                nowUtc
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(level.ToDto());
    }
}

public static class SetStockThresholdsHandler
{
    public static async Task<Result<StockLevelDto>> Handle(
        SetStockThresholdsCommand command,
        IStockRepository stock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var level = await stock.GetByCatalogItemAsync(command.TenantId, command.CatalogItemId, ct);
        if (level is null)
        {
            var created = StockLevel.Create(
                command.TenantId,
                command.CatalogItemId,
                0,
                command.MinLevel,
                command.MaxLevel,
                command.ReorderPoint,
                nowUtc
            );
            if (created.IsFailure)
                return Result.Failure<StockLevelDto>(created.Error);
            await stock.AddStockLevelAsync(created.Value, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success(created.Value.ToDto());
        }

        level.SetThresholds(command.MinLevel, command.MaxLevel, command.ReorderPoint, nowUtc);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(level.ToDto());
    }
}

// ───────────────────────── Queries ─────────────────────────

public sealed record GetStockLevelQuery(Guid TenantId, Guid CatalogItemId);

public sealed record ListStockLevelsQuery(Guid TenantId, bool LowStockOnly, int Page, int PageSize);

public sealed record ListStockMovementsQuery(Guid TenantId, Guid? CatalogItemId, int Page, int PageSize);

public static class GetStockLevelHandler
{
    public static async Task<Result<StockLevelDto>> Handle(
        GetStockLevelQuery query,
        IStockRepository stock,
        CancellationToken ct
    )
    {
        var level = await stock.GetByCatalogItemAsync(query.TenantId, query.CatalogItemId, ct);
        return level is null
            ? Result.Failure<StockLevelDto>(InventoryErrors.StockLevelNotFound)
            : Result.Success(level.ToDto());
    }
}

public static class ListStockLevelsHandler
{
    public static async Task<Result<PagedResult<StockLevelDto>>> Handle(
        ListStockLevelsQuery query,
        IStockRepository stock,
        CancellationToken ct
    )
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 200 ? 50 : query.PageSize;
        var (rows, total) = await stock.ListStockLevelsAsync(query.TenantId, query.LowStockOnly, page, pageSize, ct);
        return Result.Success(
            new PagedResult<StockLevelDto>(rows.Select(r => r.ToDto()).ToList(), total, page, pageSize)
        );
    }
}

public static class ListStockMovementsHandler
{
    public static async Task<Result<PagedResult<StockMovementDto>>> Handle(
        ListStockMovementsQuery query,
        IStockRepository stock,
        CancellationToken ct
    )
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 200 ? 50 : query.PageSize;
        var (rows, total) = await stock.ListMovementsAsync(query.TenantId, query.CatalogItemId, page, pageSize, ct);
        return Result.Success(
            new PagedResult<StockMovementDto>(rows.Select(r => r.ToDto()).ToList(), total, page, pageSize)
        );
    }
}
