using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Scribe.Api.Authorization;
using TaxVision.Scribe.Api.Common;
using TaxVision.Scribe.Application.EventMappings;
using TaxVision.Scribe.Application.EventMappings.Commands;
using TaxVision.Scribe.Application.EventMappings.Queries;
using TaxVision.Scribe.Domain;
using Wolverine;

namespace TaxVision.Scribe.Api.Controllers;

/// <summary>
/// Reglas de resolución evento→template (ej. "auth.password_reset_requested.v1" → "auth.password_reset").
/// EventKey/Scope/TenantId/Locale son la identidad de la regla; editar solo cambia a qué TemplateKey
/// apunta, su prioridad o si está habilitada — no la identidad (borrar y recrear para eso).
/// </summary>
[ApiController]
[Route("scribe/event-mappings")]
[Authorize]
public sealed class EventTemplateMappingsController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateEventTemplateMappingRequest(
        TemplateScope Scope,
        string EventKey,
        string TemplateKey,
        string? Locale,
        int Priority = 0
    );

    public sealed record UpdateEventTemplateMappingRequest(string TemplateKey, int Priority, bool Enabled);

    [HttpPost]
    [HasPermission(ScribePermissions.EventMappingsWrite)]
    [ProducesResponseType<EventTemplateMappingResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateEventTemplateMappingRequest request, CancellationToken ct)
    {
        User.TryGetTenantId(out var tenantId);
        var command = new CreateEventTemplateMappingCommand(
            request.Scope,
            request.Scope == TemplateScope.Tenant ? tenantId : null,
            User.IsPlatformAdmin(),
            request.EventKey,
            request.TemplateKey,
            request.Locale,
            request.Priority
        );
        var result = await bus.InvokeAsync<Result<EventTemplateMappingResponse>>(command, ct);
        return result.IsSuccess
            ? Created($"/scribe/event-mappings/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(ScribePermissions.EventMappingsRead)]
    [ProducesResponseType<IReadOnlyList<EventTemplateMappingResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var result = await bus.InvokeAsync<Result<IReadOnlyList<EventTemplateMappingResponse>>>(
            new GetEventTemplateMappingsQuery(tenantId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(ScribePermissions.EventMappingsRead)]
    [ProducesResponseType<EventTemplateMappingResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var result = await bus.InvokeAsync<Result<EventTemplateMappingResponse>>(
            new GetEventTemplateMappingByIdQuery(id, tenantId, User.IsPlatformAdmin()),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(ScribePermissions.EventMappingsWrite)]
    [ProducesResponseType<EventTemplateMappingResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateEventTemplateMappingRequest request,
        CancellationToken ct
    )
    {
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var command = new UpdateEventTemplateMappingCommand(
            id,
            tenantId,
            User.IsPlatformAdmin(),
            request.TemplateKey,
            request.Priority,
            request.Enabled
        );
        var result = await bus.InvokeAsync<Result<EventTemplateMappingResponse>>(command, ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(ScribePermissions.EventMappingsWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var result = await bus.InvokeAsync<Result>(
            new DeleteEventTemplateMappingCommand(id, tenantId, User.IsPlatformAdmin()),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
