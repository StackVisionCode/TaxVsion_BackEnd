using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Catalog.Application.Common;
using TaxVision.Catalog.Application.Items;
using TaxVision.Catalog.Domain.Items;
using Wolverine;

namespace TaxVision.Catalog.Api.Controllers;

/// <summary>CRUD de ítems del catálogo (productos/servicios). Tenant y usuario salen del JWT —
/// nunca del body. RBAC por permiso llega en la Fase 5.</summary>
[ApiController]
[Route("catalog/items")]
[Authorize]
[AllowActorTypes(ActorType.Service, ActorType.TenantAdmin, ActorType.TenantEmployee)]
public sealed class ItemsController(IMessageBus bus, ITenantContext tenant) : ControllerBase
{
    public sealed record AttributeRequest(string Key, string Value, string? ValueType);

    public sealed record CreateItemRequest(
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
        IReadOnlyList<AttributeRequest>? Attributes
    );

    public sealed record UpdateItemRequest(
        string Name,
        string? Description,
        string? Barcode,
        Guid CategoryId,
        string? Unit,
        string? ImageUrl,
        IReadOnlyList<AttributeRequest>? Attributes
    );

    public sealed record ChangePriceRequest(
        decimal PriceAmount,
        string PriceCurrency,
        decimal? CostAmount,
        string? CostCurrency
    );

    public sealed record SetActiveRequest(bool IsActive);

    private Guid UserId => User.TryGetUserId(out var id) ? id : Guid.Empty;

    private static List<CatalogItemAttributeDto>? Map(IReadOnlyList<AttributeRequest>? attrs) =>
        attrs?.Select(a => new CatalogItemAttributeDto(a.Key, a.Value, a.ValueType)).ToList();

    [HttpPost]
    [HasPermission(CatalogPermissions.Write)]
    [RateLimit("catalog.g.write")]
    [ProducesResponseType<CatalogItemDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateItemRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<CatalogItemDto>>(
            new CreateCatalogItemCommand(
                tenant.TenantId,
                UserId,
                request.Name,
                request.Description,
                request.Sku,
                request.Barcode,
                request.CategoryId,
                request.Kind,
                request.PriceAmount,
                request.PriceCurrency,
                request.CostAmount,
                request.CostCurrency,
                request.Unit,
                request.TrackInventory,
                request.ImageUrl,
                Map(request.Attributes)
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(CatalogPermissions.Read)]
    [RateLimit("catalog.f.read")]
    [ProducesResponseType<PagedResult<CatalogItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? categoryId,
        [FromQuery] string? search,
        [FromQuery] bool activeOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default
    )
    {
        var result = await bus.InvokeAsync<Result<PagedResult<CatalogItemDto>>>(
            new ListCatalogItemsQuery(tenant.TenantId, categoryId, search, activeOnly, page, pageSize),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(CatalogPermissions.Read)]
    [RateLimit("catalog.f.read")]
    [ProducesResponseType<CatalogItemDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<CatalogItemDto>>(new GetCatalogItemQuery(tenant.TenantId, id), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(CatalogPermissions.Write)]
    [RateLimit("catalog.g.write")]
    [ProducesResponseType<CatalogItemDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateItemRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<CatalogItemDto>>(
            new UpdateCatalogItemCommand(
                tenant.TenantId,
                id,
                request.Name,
                request.Description,
                request.Barcode,
                request.CategoryId,
                request.Unit,
                request.ImageUrl,
                Map(request.Attributes)
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}/price")]
    [HasPermission(CatalogPermissions.Write)]
    [RateLimit("catalog.g.write")]
    [ProducesResponseType<CatalogItemDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangePrice(Guid id, [FromBody] ChangePriceRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<CatalogItemDto>>(
            new ChangeCatalogItemPriceCommand(
                tenant.TenantId,
                id,
                request.PriceAmount,
                request.PriceCurrency,
                request.CostAmount,
                request.CostCurrency
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}/active")]
    [HasPermission(CatalogPermissions.Write)]
    [RateLimit("catalog.g.write")]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetActiveRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(
            new SetCatalogItemActiveCommand(tenant.TenantId, id, request.IsActive),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(CatalogPermissions.Delete)]
    [RateLimit("catalog.g.write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(new DeleteCatalogItemCommand(tenant.TenantId, id), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
