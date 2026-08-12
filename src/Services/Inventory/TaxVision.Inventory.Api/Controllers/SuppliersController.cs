using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Inventory.Application.Common;
using TaxVision.Inventory.Application.Suppliers;
using Wolverine;

namespace TaxVision.Inventory.Api.Controllers;

[ApiController]
[Route("inventory/suppliers")]
[Authorize]
[AllowActorTypes(ActorType.Service, ActorType.TenantAdmin, ActorType.TenantEmployee)]
public sealed class SuppliersController(IMessageBus bus, ITenantContext tenant) : ControllerBase
{
    public sealed record SupplierRequest(string Name, string? ContactName, string? Email, string? Phone, string? Address, string? TaxId);

    public sealed record SetActiveRequest(bool IsActive);

    private Guid UserId => User.TryGetUserId(out var id) ? id : Guid.Empty;

    [HttpPost]
    [HasPermission(InventoryPermissions.Write)]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] SupplierRequest r, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<SupplierDto>>(new CreateSupplierCommand(tenant.TenantId, UserId, r.Name, r.ContactName, r.Email, r.Phone, r.Address, r.TaxId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(InventoryPermissions.Read)]
    [ProducesResponseType<IReadOnlyList<SupplierDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool activeOnly = false, CancellationToken ct = default)
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<SupplierDto>>>(new ListSuppliersQuery(tenant.TenantId, activeOnly), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(InventoryPermissions.Read)]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<SupplierDto>>(new GetSupplierQuery(tenant.TenantId, id), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(InventoryPermissions.Write)]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SupplierRequest r, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<SupplierDto>>(new UpdateSupplierCommand(tenant.TenantId, id, r.Name, r.ContactName, r.Email, r.Phone, r.Address, r.TaxId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}/active")]
    [HasPermission(InventoryPermissions.Write)]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetActiveRequest r, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(new SetSupplierActiveCommand(tenant.TenantId, id, r.IsActive), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(InventoryPermissions.Write)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(new DeleteSupplierCommand(tenant.TenantId, id), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}

[ApiController]
[Route("inventory/item-suppliers")]
[Authorize]
[AllowActorTypes(ActorType.Service, ActorType.TenantAdmin, ActorType.TenantEmployee)]
public sealed class ItemSuppliersController(IMessageBus bus, ITenantContext tenant) : ControllerBase
{
    public sealed record UpsertRequest(Guid CatalogItemId, Guid SupplierId, string? SupplierSku, decimal? PriceAmount, string? PriceCurrency, int? LeadTimeDays, bool IsPreferred);

    [HttpPost]
    [HasPermission(InventoryPermissions.Write)]
    [ProducesResponseType<ItemSupplierDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Upsert([FromBody] UpsertRequest r, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<ItemSupplierDto>>(
            new UpsertItemSupplierCommand(tenant.TenantId, r.CatalogItemId, r.SupplierId, r.SupplierSku, r.PriceAmount, r.PriceCurrency, r.LeadTimeDays, r.IsPreferred), ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(InventoryPermissions.Read)]
    [ProducesResponseType<IReadOnlyList<ItemSupplierDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] Guid catalogItemId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<ItemSupplierDto>>>(new ListItemSuppliersQuery(tenant.TenantId, catalogItemId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(InventoryPermissions.Write)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(new DeleteItemSupplierCommand(tenant.TenantId, id), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
