using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.DataCollection.Engine.Validation;
using TaxVision.Tasks.Api.Authorization;
using TaxVision.Tasks.Api.Filters;
using TaxVision.Tasks.Api.Requests.ClientForms;
using TaxVision.Tasks.ClientForms.Application.Templates.Commands;
using TaxVision.Tasks.ClientForms.Application.Templates.Queries;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers.ClientForms;

[ApiController]
[ClientFormsEndpoint]
[Route("tasks/forms/templates")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class FormTemplatesController(IMessageBus bus, FormPublisherCapabilitiesResolver capabilities)
    : ControllerBase
{
    [HttpGet]
    [HasPermission(TasksPermissions.FormTemplatesRead)]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> List(
        [FromQuery] FormTemplateStatus? status,
        [FromQuery] FormPurpose? purpose,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();
        var result = await bus.InvokeAsync<PagedResult<FormTemplateSummaryResponse>>(
            new ListFormTemplatesQuery(tenantId, status, purpose, search, page, pageSize),
            ct
        );
        return Ok(result);
    }

    // Quien asigna, quien envía a externos y quien arma intake links elige plantilla publicada.
    [HttpGet("assignable")]
    [HasAnyPermission(
        TasksPermissions.FormRequestsManage,
        TasksPermissions.FormRequestsSendExternal,
        TasksPermissions.FormIntakeLinksManage
    )]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> ListAssignable([FromQuery] string? search, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();
        var result = await bus.InvokeAsync<IReadOnlyList<AssignableFormTemplateResponse>>(
            new ListAssignableFormTemplatesQuery(tenantId, search),
            ct
        );
        return Ok(result);
    }

    [HttpPost]
    [HasPermission(TasksPermissions.FormTemplatesManage)]
    [RateLimit("task.h.form_templates_write")]
    public async Task<IActionResult> Create(CreateFormTemplateRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        var result = await bus.InvokeAsync<Result<FormTemplateDetailResponse>>(
            new CreateFormTemplateCommand(
                tenantId,
                userId,
                request.Name,
                request.Description,
                request.Purpose,
                request.Presentation
            ),
            ct
        );
        return result.IsFailure
            ? StatusCode(result.Error.ToHttpStatusCode(), result.Error)
            : CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(TasksPermissions.FormTemplatesRead)]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();
        var result = await bus.InvokeAsync<Result<FormTemplateDetailResponse>>(
            new GetFormTemplateQuery(tenantId, id),
            ct
        );
        return ToActionResult(result);
    }

    [HttpPatch("{id:guid}")]
    [HasPermission(TasksPermissions.FormTemplatesManage)]
    [RateLimit("task.h.form_templates_write")]
    public async Task<IActionResult> Rename(Guid id, RenameFormTemplateRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!TryDecode(request.RowVersion, out var rowVersion))
            return BadRequest(RowVersionError);
        var result = await bus.InvokeAsync<Result<FormTemplateDetailResponse>>(
            new RenameFormTemplateCommand(tenantId, userId, id, request.Name, request.Description, rowVersion),
            ct
        );
        return ToActionResult(result);
    }

    [HttpPut("{id:guid}/draft")]
    [RequestSizeLimit(600_000)]
    [HasPermission(TasksPermissions.FormTemplatesManage)]
    [RateLimit("task.h.form_draft_write")]
    public async Task<IActionResult> UpdateDraft(Guid id, UpdateFormTemplateDraftRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!TryDecode(request.RowVersion, out var rowVersion))
            return BadRequest(RowVersionError);
        var result = await bus.InvokeAsync<Result<UpdateFormTemplateDraftOutcome>>(
            new UpdateFormTemplateDraftCommand(
                tenantId,
                userId,
                id,
                request.Definition.GetRawText(),
                rowVersion,
                await capabilities.CanConfigureActionsAsync(User, ct)
            ),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);
        return result.Value.Updated ? Ok(result.Value) : InvalidDefinition(result.Value.Issues);
    }

    [HttpPost("{id:guid}/validate")]
    [HasPermission(TasksPermissions.FormTemplatesRead)]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> Validate(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();
        var result = await bus.InvokeAsync<Result<FormTemplateValidationResponse>>(
            new ValidateFormTemplateQuery(tenantId, id, await capabilities.ResolveAsync(User, ct)),
            ct
        );
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/publish")]
    [HasPermission(TasksPermissions.FormTemplatesPublish)]
    [RateLimit("task.h.form_templates_write")]
    public async Task<IActionResult> Publish(Guid id, FormTemplateRowVersionRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!TryDecode(request.RowVersion, out var rowVersion))
            return BadRequest(RowVersionError);
        var result = await bus.InvokeAsync<Result<PublishFormTemplateOutcome>>(
            new PublishFormTemplateCommand(tenantId, userId, id, rowVersion, await capabilities.ResolveAsync(User, ct)),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);
        return result.Value.Published ? Ok(result.Value.Version) : InvalidDefinition(result.Value.Issues);
    }

    [HttpPost("{id:guid}/archive")]
    [HasPermission(TasksPermissions.FormTemplatesManage)]
    [RateLimit("task.h.form_templates_write")]
    public async Task<IActionResult> Archive(Guid id, FormTemplateRowVersionRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!TryDecode(request.RowVersion, out var rowVersion))
            return BadRequest(RowVersionError);
        var result = await bus.InvokeAsync<Result<FormTemplateDetailResponse>>(
            new ArchiveFormTemplateCommand(tenantId, userId, id, rowVersion),
            ct
        );
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/restore")]
    [HasPermission(TasksPermissions.FormTemplatesManage)]
    [RateLimit("task.h.form_templates_write")]
    public async Task<IActionResult> Restore(Guid id, FormTemplateRowVersionRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!TryDecode(request.RowVersion, out var rowVersion))
            return BadRequest(RowVersionError);
        var result = await bus.InvokeAsync<Result<FormTemplateDetailResponse>>(
            new RestoreFormTemplateCommand(tenantId, userId, id, rowVersion),
            ct
        );
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/duplicate")]
    [HasPermission(TasksPermissions.FormTemplatesManage)]
    [RateLimit("task.h.form_templates_write")]
    public async Task<IActionResult> Duplicate(Guid id, DuplicateFormTemplateRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        var result = await bus.InvokeAsync<Result<FormTemplateDetailResponse>>(
            new DuplicateFormTemplateCommand(
                tenantId,
                userId,
                id,
                request.FromVersionNumber,
                await capabilities.CanConfigureActionsAsync(User, ct)
            ),
            ct
        );
        return result.IsFailure
            ? StatusCode(result.Error.ToHttpStatusCode(), result.Error)
            : CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }

    [HttpPost("{id:guid}/revert")]
    [HasPermission(TasksPermissions.FormTemplatesManage)]
    [RateLimit("task.h.form_templates_write")]
    public async Task<IActionResult> Revert(Guid id, RevertFormTemplateDraftRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!TryDecode(request.RowVersion, out var rowVersion))
            return BadRequest(RowVersionError);
        var result = await bus.InvokeAsync<Result<FormTemplateDetailResponse>>(
            new RevertFormTemplateDraftCommand(
                tenantId,
                userId,
                id,
                request.VersionNumber,
                rowVersion,
                await capabilities.CanConfigureActionsAsync(User, ct)
            ),
            ct
        );
        return ToActionResult(result);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(TasksPermissions.FormTemplatesManage)]
    [RateLimit("task.h.form_templates_write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        var result = await bus.InvokeAsync<Result>(new DeleteFormTemplateCommand(tenantId, userId, id), ct);
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : NoContent();
    }

    [HttpGet("{id:guid}/versions")]
    [HasPermission(TasksPermissions.FormTemplatesRead)]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> Versions(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();
        return ToActionResult(
            await bus.InvokeAsync<Result<IReadOnlyList<FormTemplateVersionSummaryResponse>>>(
                new ListFormTemplateVersionsQuery(tenantId, id),
                ct
            )
        );
    }

    [HttpGet("{id:guid}/versions/{versionNumber:int}")]
    [HasPermission(TasksPermissions.FormTemplatesRead)]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> Version(Guid id, int versionNumber, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();
        var result = await bus.InvokeAsync<Result<FormTemplateVersionResponse>>(
            new GetFormTemplateVersionQuery(tenantId, id, versionNumber),
            ct
        );
        return ToActionResult(result);
    }

    private IActionResult ToActionResult<T>(Result<T> result) =>
        result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);

    private ObjectResult InvalidDefinition(IReadOnlyList<ValidationIssue> issues) =>
        UnprocessableEntity(
            new
            {
                code = "FormTemplate.InvalidDefinition",
                message = "The form has validation issues.",
                issues,
            }
        );

    private static bool TryDecode(string? value, out byte[] rowVersion)
    {
        try
        {
            rowVersion = Convert.FromBase64String(value ?? string.Empty);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            rowVersion = [];
            return false;
        }
    }

    private static readonly Error RowVersionError = new(
        "FormTemplate.InvalidRowVersion",
        "Row version must be a valid base64 value."
    );
}
