using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Inventory.Application.Common;
using TaxVision.Inventory.Application.Stock;
using TaxVision.Inventory.Domain.Stock;
using Wolverine;

namespace TaxVision.Inventory.Api.Controllers;

/// <summary>Stock por ítem de catálogo + ledger de movimientos. Tenant/usuario del JWT.</summary>
[ApiController]
[Route("inventory/stock")]
[Authorize]
[AllowActorTypes(ActorType.Service, ActorType.TenantAdmin, ActorType.TenantEmployee)]
public sealed class StockController(IMessageBus bus, ITenantContext tenant) : ControllerBase
{
    public sealed record AdjustRequest(StockMovementType Type, int Quantity, string? Reference, string? Notes);

    public sealed record ThresholdsRequest(int MinLevel, int MaxLevel, int ReorderPoint);

    private Guid UserId => User.TryGetUserId(out var id) ? id : Guid.Empty;

    [HttpPost("{catalogItemId:guid}/adjust")]
    [HasPermission(InventoryPermissions.Adjust)]
    [RateLimit("inventory.g.adjust")]
    [ProducesResponseType<StockLevelDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Adjust(Guid catalogItemId, [FromBody] AdjustRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<StockLevelDto>>(
            new AdjustStockCommand(
                tenant.TenantId,
                UserId,
                catalogItemId,
                request.Type,
                request.Quantity,
                request.Reference,
                request.Notes
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{catalogItemId:guid}/thresholds")]
    [HasPermission(InventoryPermissions.Write)]
    [RateLimit("inventory.g.write")]
    [ProducesResponseType<StockLevelDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetThresholds(
        Guid catalogItemId,
        [FromBody] ThresholdsRequest request,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<StockLevelDto>>(
            new SetStockThresholdsCommand(
                tenant.TenantId,
                catalogItemId,
                request.MinLevel,
                request.MaxLevel,
                request.ReorderPoint
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("movements")]
    [HasPermission(InventoryPermissions.Read)]
    [RateLimit("inventory.f.read")]
    [ProducesResponseType<PagedResult<StockMovementDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Movements(
        [FromQuery] Guid? catalogItemId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default
    )
    {
        var result = await bus.InvokeAsync<Result<PagedResult<StockMovementDto>>>(
            new ListStockMovementsQuery(tenant.TenantId, catalogItemId, page, pageSize),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{catalogItemId:guid}")]
    [HasPermission(InventoryPermissions.Read)]
    [RateLimit("inventory.f.read")]
    [ProducesResponseType<StockLevelDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid catalogItemId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<StockLevelDto>>(
            new GetStockLevelQuery(tenant.TenantId, catalogItemId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(InventoryPermissions.Read)]
    [RateLimit("inventory.f.read")]
    [ProducesResponseType<PagedResult<StockLevelDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] bool lowStockOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default
    )
    {
        var result = await bus.InvokeAsync<Result<PagedResult<StockLevelDto>>>(
            new ListStockLevelsQuery(tenant.TenantId, lowStockOnly, page, pageSize),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
