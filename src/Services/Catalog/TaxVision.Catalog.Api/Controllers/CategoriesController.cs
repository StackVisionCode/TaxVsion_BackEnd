using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Catalog.Application.Categories;
using TaxVision.Catalog.Application.Common;
using Wolverine;

namespace TaxVision.Catalog.Api.Controllers;

/// <summary>CRUD de categorías (árbol). Tenant y usuario salen del JWT — nunca del body.</summary>
[ApiController]
[Route("catalog/categories")]
[Authorize]
[AllowActorTypes(ActorType.Service, ActorType.TenantAdmin, ActorType.TenantEmployee)]
public sealed class CategoriesController(IMessageBus bus, ITenantContext tenant) : ControllerBase
{
    public sealed record CreateCategoryRequest(string Name, string? Description, Guid? ParentCategoryId);

    public sealed record UpdateCategoryRequest(string Name, string? Description, Guid? ParentCategoryId);

    public sealed record SetActiveRequest(bool IsActive);

    private Guid UserId => User.TryGetUserId(out var id) ? id : Guid.Empty;

    [HttpPost]
    [HasPermission(CatalogPermissions.Write)]
    [RateLimit("catalog.g.write")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<CategoryDto>>(
            new CreateCategoryCommand(tenant.TenantId, UserId, request.Name, request.Description, request.ParentCategoryId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(CatalogPermissions.Read)]
    [RateLimit("catalog.f.read")]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool activeOnly = false, CancellationToken ct = default)
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<CategoryDto>>>(
            new ListCategoriesQuery(tenant.TenantId, activeOnly),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(CatalogPermissions.Read)]
    [RateLimit("catalog.f.read")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<CategoryDto>>(new GetCategoryQuery(tenant.TenantId, id), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(CatalogPermissions.Write)]
    [RateLimit("catalog.g.write")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCategoryRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<CategoryDto>>(
            new UpdateCategoryCommand(tenant.TenantId, id, request.Name, request.Description, request.ParentCategoryId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}/active")]
    [HasPermission(CatalogPermissions.Write)]
    [RateLimit("catalog.g.write")]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetActiveRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(new SetCategoryActiveCommand(tenant.TenantId, id, request.IsActive), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(CatalogPermissions.Delete)]
    [RateLimit("catalog.g.write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(new DeleteCategoryCommand(tenant.TenantId, id), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
