using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Scribe.Api.Authorization;
using TaxVision.Scribe.Api.Common;
using TaxVision.Scribe.Application.Layouts;
using TaxVision.Scribe.Application.Layouts.Commands;
using TaxVision.Scribe.Domain;
using Wolverine;

namespace TaxVision.Scribe.Api.Controllers;

/// <summary>CRUD de EmailLayout/EmailLayoutVersion (Fase 5).</summary>
[ApiController]
[Route("scribe/layouts")]
[Authorize]
public sealed class EmailLayoutsController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateEmailLayoutRequest(
        TemplateScope Scope,
        string LayoutKey,
        string Name,
        string? Description
    );

    public sealed record AddDraftVersionRequest(string HtmlContent, string? DesignJson);

    [HttpPost]
    [HasPermission(ScribePermissions.LayoutsWrite)]
    [ProducesResponseType<EmailLayoutResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateEmailLayoutRequest request, CancellationToken ct)
    {
        User.TryGetUserId(out var userId);
        User.TryGetTenantId(out var tenantId);
        var command = new CreateEmailLayoutCommand(
            request.Scope,
            request.Scope == TemplateScope.Tenant ? tenantId : null,
            User.IsPlatformAdmin(),
            request.LayoutKey,
            request.Name,
            request.Description,
            userId
        );
        var result = await bus.InvokeAsync<Result<EmailLayoutResponse>>(command, ct);
        return result.IsSuccess
            ? Created($"/scribe/layouts/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/versions")]
    [HasPermission(ScribePermissions.LayoutsWrite)]
    [ProducesResponseType<EmailLayoutVersionResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddDraftVersion(
        Guid id,
        [FromBody] AddDraftVersionRequest request,
        CancellationToken ct
    )
    {
        User.TryGetUserId(out var userId);
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var command = new AddEmailLayoutDraftVersionCommand(
            id,
            tenantId,
            User.IsPlatformAdmin(),
            request.HtmlContent,
            request.DesignJson,
            userId
        );
        var result = await bus.InvokeAsync<Result<EmailLayoutVersionResponse>>(command, ct);
        return result.IsSuccess
            ? Created($"/scribe/layouts/{id}/versions/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/versions/{versionId:guid}/publish")]
    [HasPermission(ScribePermissions.LayoutsWrite)]
    [ProducesResponseType<EmailLayoutVersionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PublishVersion(Guid id, Guid versionId, CancellationToken ct)
    {
        User.TryGetUserId(out var userId);
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var command = new PublishEmailLayoutVersionCommand(id, versionId, tenantId, User.IsPlatformAdmin(), userId);
        var result = await bus.InvokeAsync<Result<EmailLayoutVersionResponse>>(command, ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
