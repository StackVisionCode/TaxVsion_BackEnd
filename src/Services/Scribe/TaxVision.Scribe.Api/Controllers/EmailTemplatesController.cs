using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Scribe.Application.Templates;
using TaxVision.Scribe.Application.Templates.Commands;
using TaxVision.Scribe.Application.Templates.Validation;
using TaxVision.Scribe.Domain;
using Wolverine;

namespace TaxVision.Scribe.Api.Controllers;

/// <summary>CRUD de EmailTemplate/EmailTemplateVersion + preview/validate (Fase 5).</summary>
[ApiController]
[Route("scribe/templates")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class EmailTemplatesController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateEmailTemplateRequest(
        TemplateScope Scope,
        string TemplateKey,
        string Name,
        string? Description
    );

    public sealed record AddDraftVersionRequest(
        string Subject,
        string HtmlContent,
        string? TextContent,
        string? DesignJson,
        Guid LayoutId,
        int LayoutVersionNumber,
        IReadOnlyList<VariableDefinitionInput> VariableDefinitions
    );

    public sealed record PreviewRequest(IReadOnlyDictionary<string, object?> SampleVariables);

    [HttpPost]
    [HasPermission(ScribePermissions.TemplatesWrite)]
    [ProducesResponseType<EmailTemplateResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateEmailTemplateRequest request, CancellationToken ct)
    {
        User.TryGetUserId(out var userId);
        User.TryGetTenantId(out var tenantId);
        var command = new CreateEmailTemplateCommand(
            request.Scope,
            request.Scope == TemplateScope.Tenant ? tenantId : null,
            User.IsPlatformAdmin(),
            request.TemplateKey,
            request.Name,
            request.Description,
            userId
        );
        var result = await bus.InvokeAsync<Result<EmailTemplateResponse>>(command, ct);
        return result.IsSuccess
            ? Created($"/scribe/templates/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/versions")]
    [HasPermission(ScribePermissions.TemplatesWrite)]
    [ProducesResponseType<EmailTemplateVersionResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddDraftVersion(
        Guid id,
        [FromBody] AddDraftVersionRequest request,
        CancellationToken ct
    )
    {
        User.TryGetUserId(out var userId);
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var command = new AddEmailTemplateDraftVersionCommand(
            id,
            tenantId,
            User.IsPlatformAdmin(),
            request.Subject,
            request.HtmlContent,
            request.TextContent,
            request.DesignJson,
            request.LayoutId,
            request.LayoutVersionNumber,
            request.VariableDefinitions,
            userId
        );
        var result = await bus.InvokeAsync<Result<EmailTemplateVersionResponse>>(command, ct);
        return result.IsSuccess
            ? Created($"/scribe/templates/{id}/versions/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/versions/{versionId:guid}/publish")]
    [HasPermission(ScribePermissions.TemplatesWrite)]
    [ProducesResponseType<EmailTemplateVersionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PublishVersion(Guid id, Guid versionId, CancellationToken ct)
    {
        User.TryGetUserId(out var userId);
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var command = new PublishEmailTemplateVersionCommand(id, versionId, tenantId, User.IsPlatformAdmin(), userId);
        var result = await bus.InvokeAsync<Result<EmailTemplateVersionResponse>>(command, ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/versions/{versionId:guid}/preview")]
    [HasPermission(ScribePermissions.TemplatesRead)]
    [ProducesResponseType<PreviewTemplateResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(
        Guid id,
        Guid versionId,
        [FromBody] PreviewRequest request,
        CancellationToken ct
    )
    {
        Guid? tenantId = User.TryGetTenantId(out var t1) ? t1 : null;
        var result = await bus.InvokeAsync<Result<PreviewTemplateResponse>>(
            new PreviewTemplateVersionQuery(versionId, request.SampleVariables, tenantId, User.IsPlatformAdmin()),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/versions/{versionId:guid}/validate")]
    [HasPermission(ScribePermissions.TemplatesRead)]
    [ProducesResponseType<TemplateValidationOutcome>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Validate(Guid id, Guid versionId, CancellationToken ct)
    {
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var result = await bus.InvokeAsync<Result<TemplateValidationOutcome>>(
            new ValidateTemplateVersionQuery(versionId, tenantId, User.IsPlatformAdmin()),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
