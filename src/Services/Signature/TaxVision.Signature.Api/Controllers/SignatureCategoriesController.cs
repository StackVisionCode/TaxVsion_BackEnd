using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Signature.Api.Requests;
using TaxVision.Signature.Application.Categories;
using TaxVision.Signature.Application.Categories.Commands.Archive;
using TaxVision.Signature.Application.Categories.Commands.Create;
using TaxVision.Signature.Application.Categories.Commands.Rename;
using TaxVision.Signature.Application.Categories.Queries.List;
using Wolverine;

namespace TaxVision.Signature.Api.Controllers;

/// <summary>
/// Categorías de firma del tenant (14.5). El listado combina las de sistema con las custom. Crear/
/// renombrar/archivar reusa el permiso de autoría de solicitudes (signature.request.create); leer usa
/// el de lectura. La categoría se guarda en cada solicitud como texto, así que archivar no toca el histórico.
/// </summary>
[ApiController]
[Route("signature/categories")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class SignatureCategoriesController(IMessageBus bus) : ControllerBase
{
    // ---------- GET /signature/categories ----------
    [HttpGet]
    [HasPermission(SignaturePermissions.RequestRead)]
    [RateLimit("signature.f.request_read")]
    [ProducesResponseType<ListSignatureCategoriesResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ListSignatureCategoriesResult>> List(
        [FromQuery] bool includeArchived = false,
        CancellationToken ct = default
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<ListSignatureCategoriesResult>(
            new ListSignatureCategoriesQuery(tenantId, includeArchived),
            ct
        );
        return Ok(result);
    }

    // ---------- POST /signature/categories ----------
    [HttpPost]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [RateLimit("signature.g.request_manage")]
    [ProducesResponseType<SignatureCategoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateSignatureCategoryBody body, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SignatureCategoryResponse>>(
            new CreateSignatureCategoryCommand(tenantId, userId, body.Name),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- PUT /signature/categories/{id} ----------
    [HttpPut("{id:guid}")]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [RateLimit("signature.g.request_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Rename(
        [FromRoute] Guid id,
        [FromBody] RenameSignatureCategoryBody body,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new RenameSignatureCategoryCommand(tenantId, id, body.Name), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- POST /signature/categories/{id}/archive ----------
    [HttpPost("{id:guid}/archive")]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [RateLimit("signature.g.request_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> Archive([FromRoute] Guid id, CancellationToken ct) =>
        SetArchived(id, archived: true, ct);

    // ---------- POST /signature/categories/{id}/unarchive ----------
    [HttpPost("{id:guid}/unarchive")]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [RateLimit("signature.g.request_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> Unarchive([FromRoute] Guid id, CancellationToken ct) =>
        SetArchived(id, archived: false, ct);

    private async Task<IActionResult> SetArchived(Guid id, bool archived, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new SetSignatureCategoryArchivedCommand(tenantId, id, archived), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
