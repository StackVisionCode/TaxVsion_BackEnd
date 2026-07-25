using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Signature.Api.Requests;
using TaxVision.Signature.Application.Requests;
using TaxVision.Signature.Application.Templates;
using TaxVision.Signature.Application.Templates.Commands.AddSlot;
using TaxVision.Signature.Application.Templates.Commands.Archive;
using TaxVision.Signature.Application.Templates.Commands.Create;
using TaxVision.Signature.Application.Templates.Commands.Instantiate;
using TaxVision.Signature.Application.Templates.Commands.PlaceField;
using TaxVision.Signature.Application.Templates.Commands.PublishTemplate;
using TaxVision.Signature.Application.Templates.Commands.RemoveField;
using TaxVision.Signature.Application.Templates.Commands.RemoveSlot;
using TaxVision.Signature.Application.Templates.Commands.UpdateDefaults;
using TaxVision.Signature.Application.Templates.Commands.UpdateMetadata;
using TaxVision.Signature.Application.Templates.Queries.GetById;
using TaxVision.Signature.Application.Templates.Queries.List;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Templates;
using Wolverine;

namespace TaxVision.Signature.Api.Controllers;

/// <summary>
/// Endpoints staff del ciclo de vida de plantillas de firma. TenantId siempre del JWT.
/// Cada endpoint tiene su método privado por fase (extracción de identidad, invocación
/// del bus, mapeo de errores) — sin acumular responsabilidad.
/// </summary>
[ApiController]
[Route("signature/templates")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class SignatureTemplatesController(IMessageBus bus) : ControllerBase
{
    // ---------- POST /signature/templates ----------
    [HttpPost]
    [HasPermission(SignaturePermissions.TemplateCreate)]
    [ProducesResponseType<SignatureTemplateResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateTemplateBody body, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var cmd = new CreateSignatureTemplateCommand(
            tenantId,
            userId,
            body.Title,
            body.Description,
            body.Category,
            body.DefaultTokenExpirationHours,
            body.RequiresSequentialSigning,
            body.RequiresConsent,
            body.GenerateCertificate
        );
        var result = await bus.InvokeAsync<Result<SignatureTemplateResponse>>(cmd, ct);
        return result.IsSuccess
            ? Created($"/signature/templates/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- GET /signature/templates ----------
    [HttpGet]
    [HasPermission(SignaturePermissions.TemplateCreate)]
    [ProducesResponseType<ListTemplatesResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ListTemplatesResult>> List(
        [FromQuery] SignatureTemplateStatus? status = null,
        [FromQuery] SignatureCategory? category = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<ListTemplatesResult>(
            new ListTemplatesQuery(tenantId, status, category, page, size),
            ct
        );
        return Ok(result);
    }

    // ---------- GET /signature/templates/{id} ----------
    [HttpGet("{id:guid}")]
    [HasPermission(SignaturePermissions.TemplateCreate)]
    [ProducesResponseType<SignatureTemplateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<SignatureTemplateResponse?>(new GetTemplateByIdQuery(tenantId, id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    // ---------- PUT /signature/templates/{id}/metadata ----------
    [HttpPut("{id:guid}/metadata")]
    [HasPermission(SignaturePermissions.TemplateUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateMetadata(
        [FromRoute] Guid id,
        [FromBody] UpdateTemplateMetadataBody body,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new UpdateTemplateMetadataCommand(tenantId, id, body.Title, body.Description, body.Category),
            ct
        );
        return MapResult(result);
    }

    // ---------- PUT /signature/templates/{id}/defaults ----------
    [HttpPut("{id:guid}/defaults")]
    [HasPermission(SignaturePermissions.TemplateUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateDefaults(
        [FromRoute] Guid id,
        [FromBody] UpdateTemplateDefaultsBody body,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new UpdateTemplateDefaultsCommand(
                tenantId,
                id,
                body.DefaultTokenExpirationHours,
                body.RequiresSequentialSigning,
                body.RequiresConsent,
                body.GenerateCertificate
            ),
            ct
        );
        return MapResult(result);
    }

    // ---------- POST /signature/templates/{id}/slots ----------
    [HttpPost("{id:guid}/slots")]
    [HasPermission(SignaturePermissions.TemplateUpdate)]
    [ProducesResponseType<TemplateSlotCreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddSlot(
        [FromRoute] Guid id,
        [FromBody] AddTemplateSlotBody body,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TemplateSlotCreatedResponse>>(
            new AddTemplateSlotCommand(tenantId, id, body.Role, body.DefaultLanguage),
            ct
        );
        return result.IsSuccess
            ? Created($"/signature/templates/{id}/slots/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- DELETE /signature/templates/{id}/slots/{slotOrder} ----------
    [HttpDelete("{id:guid}/slots/{slotOrder:int}")]
    [HasPermission(SignaturePermissions.TemplateUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RemoveSlot([FromRoute] Guid id, [FromRoute] int slotOrder, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new RemoveTemplateSlotCommand(tenantId, id, slotOrder), ct);
        return MapResult(result);
    }

    // ---------- POST /signature/templates/{id}/fields ----------
    [HttpPost("{id:guid}/fields")]
    [HasPermission(SignaturePermissions.TemplateUpdate)]
    [ProducesResponseType<TemplateFieldCreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PlaceField(
        [FromRoute] Guid id,
        [FromBody] PlaceTemplateFieldBody body,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var cmd = new PlaceTemplateFieldCommand(
            tenantId,
            id,
            body.SlotOrder,
            body.Kind,
            body.Page,
            body.X,
            body.Y,
            body.Width,
            body.Height,
            body.Label,
            body.IsRequired
        );
        var result = await bus.InvokeAsync<Result<TemplateFieldCreatedResponse>>(cmd, ct);
        return result.IsSuccess
            ? Created($"/signature/templates/{id}/fields/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- DELETE /signature/templates/{id}/fields/{fieldId} ----------
    [HttpDelete("{id:guid}/fields/{fieldId:guid}")]
    [HasPermission(SignaturePermissions.TemplateUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RemoveField([FromRoute] Guid id, [FromRoute] Guid fieldId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new RemoveTemplateFieldCommand(tenantId, id, fieldId), ct);
        return MapResult(result);
    }

    // ---------- POST /signature/templates/{id}/publish ----------
    [HttpPost("{id:guid}/publish")]
    [HasPermission(SignaturePermissions.TemplateUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Publish([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new PublishTemplateCommand(tenantId, id), ct);
        return MapResult(result);
    }

    // ---------- POST /signature/templates/{id}/archive ----------
    [HttpPost("{id:guid}/archive")]
    [HasPermission(SignaturePermissions.TemplateDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Archive([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new ArchiveTemplateCommand(tenantId, id), ct);
        return MapResult(result);
    }

    // ---------- POST /signature/templates/{id}/instantiate ----------
    [HttpPost("{id:guid}/instantiate")]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [ProducesResponseType<SignatureRequestResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Instantiate(
        [FromRoute] Guid id,
        [FromBody] InstantiateTemplateBody body,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var cmd = new CreateSignatureRequestFromTemplateCommand(
            tenantId,
            userId,
            id,
            body.OriginalFileId,
            body.SlotBindings,
            body.DescriptionOverride
        );
        var result = await bus.InvokeAsync<Result<SignatureRequestResponse>>(cmd, ct);
        return result.IsSuccess
            ? Created($"/signature/requests/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno una única responsabilidad
    // ------------------------------------------------------------------

    private IActionResult MapResult(Result result) =>
        result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
}
