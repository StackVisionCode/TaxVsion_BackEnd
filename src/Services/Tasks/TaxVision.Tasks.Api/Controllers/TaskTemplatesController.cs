using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Requests;
using TaxVision.Tasks.Application.Templates;
using TaxVision.Tasks.Application.Templates.Commands;
using TaxVision.Tasks.Application.Templates.Queries;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers;

/// <summary>
/// El guion de los encargos. Editarlo pide <c>tasks.templates.manage</c> —lo arma quien define cómo
/// trabaja la firma—; aplicarlo pide sólo <c>tasks.write</c>, porque es el gesto diario del preparador.
/// </summary>
[ApiController]
[Route("tasks/templates")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class TaskTemplatesController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    [HasPermission(TasksPermissions.TemplatesManage)]
    [RateLimit("task.h.templates_write")]
    public async Task<IActionResult> Create([FromBody] SaveTaskTemplateRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<TaskTemplateResponse>>(
            new CreateTaskTemplateCommand(
                tenantId,
                userId,
                request.Name,
                request.Description,
                request.RecurrenceRule,
                request.RecurrenceTimeZoneId,
                request.RecurrenceMode,
                ToDrafts(request)
            ),
            ct
        );

        return result.IsFailure
            ? StatusCode(result.Error.ToHttpStatusCode(), result.Error)
            : CreatedAtAction(nameof(GetById), new { templateId = result.Value.Id }, result.Value);
    }

    /// <summary>Copia el catálogo fiscal estándar al tenant. Idempotente: salta las que ya existen.</summary>
    [HttpPost("install-standard")]
    [HasPermission(TasksPermissions.TemplatesManage)]
    [RateLimit("task.h.templates_write")]
    public async Task<IActionResult> InstallStandard(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<TaskTemplateResponse>>>(
            new InstallStandardTaskTemplatesCommand(tenantId, userId),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpPut("{templateId:guid}")]
    [HasPermission(TasksPermissions.TemplatesManage)]
    [RateLimit("task.h.templates_write")]
    public async Task<IActionResult> Replace(
        Guid templateId,
        [FromBody] SaveTaskTemplateRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();

        var result = await bus.InvokeAsync<Result<TaskTemplateResponse>>(
            new ReplaceTaskTemplateStepsCommand(
                tenantId,
                templateId,
                request.Name,
                request.Description,
                request.RecurrenceRule,
                request.RecurrenceTimeZoneId,
                request.RecurrenceMode,
                ToDrafts(request)
            ),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpPost("{templateId:guid}/active")]
    [HasPermission(TasksPermissions.TemplatesManage)]
    [RateLimit("task.h.templates_write")]
    public async Task<IActionResult> SetActive(
        Guid templateId,
        [FromBody] SetTaskTemplateActiveRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(
            new SetTaskTemplateActiveCommand(tenantId, templateId, request.IsActive),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : NoContent();
    }

    /// <summary>
    /// Los archivos de referencia del guion —checklist, formulario en blanco—. Cada instancia los
    /// recibe con el mismo <c>fileId</c>: el byte vive una sola vez en CloudStorage.
    /// </summary>
    [HttpPut("{templateId:guid}/attachments")]
    [HasPermission(TasksPermissions.TemplatesManage)]
    [RateLimit("task.h.templates_write")]
    public async Task<IActionResult> ReplaceAttachments(
        Guid templateId,
        [FromBody] SaveTaskTemplateAttachmentsRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();

        var result = await bus.InvokeAsync<Result<TaskTemplateResponse>>(
            new ReplaceTaskTemplateAttachmentsCommand(
                tenantId,
                templateId,
                [
                    .. request.Attachments.Select(a => new TaskTemplateAttachmentDraft(
                        a.FileId,
                        a.DisplayName,
                        a.ContentType,
                        a.SizeBytes,
                        a.StepOrder
                    )),
                ]
            ),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    /// <summary>El gesto diario: el preparador toma un cliente y le aplica el guion del encargo.</summary>
    [HttpPost("{templateId:guid}/apply")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.h.templates_apply")]
    public async Task<IActionResult> Apply(
        Guid templateId,
        [FromBody] ApplyTaskTemplateRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<TemplateApplicationResponse>>(
            new ApplyTaskTemplateCommand(
                tenantId,
                userId,
                templateId,
                request.AssigneeUserId,
                request.CustomerId,
                request.TaxYear,
                request.DueAtUtc,
                request.TimeZoneId,
                request.AllowDuplicate
            ),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpGet("{templateId:guid}")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    public async Task<IActionResult> GetById(Guid templateId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();

        var result = await bus.InvokeAsync<Result<TaskTemplateResponse>>(
            new GetTaskTemplateByIdQuery(tenantId, templateId),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpGet]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    public async Task<IActionResult> List([FromQuery] bool onlyActive = true, CancellationToken ct = default)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<TaskTemplateResponse>>>(
            new ListTaskTemplatesQuery(tenantId, onlyActive),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    private static IReadOnlyList<TaskTemplateStepDraft> ToDrafts(SaveTaskTemplateRequest request) =>
        [
            .. request.Steps.Select(s => new TaskTemplateStepDraft(
                s.Order,
                s.Title,
                s.Description,
                s.Priority,
                s.EstimatedHours,
                s.DueOffsetDays,
                s.IsStatutory,
                s.DependsOnStepOrder,
                s.ParentStepOrder,
                s.SuggestedRoleName
            )),
        ];
}
