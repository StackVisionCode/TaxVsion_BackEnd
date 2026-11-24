using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TaxVision.Tasks.Api.Requests;
using TaxVision.Tasks.Application.Statuses;
using TaxVision.Tasks.Application.Statuses.Commands;
using TaxVision.Tasks.Application.Statuses.Queries;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers;

/// <summary>
/// Estados de la oficina. Leerlos es de todo el equipo; gestionarlos, de quien tiene
/// <c>tasks.statuses.manage</c>. El identificador público es el código.
/// </summary>
[ApiController]
[Route("tasks/statuses")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class TaskStatusesController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<WorkflowStatusesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeArchived, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        return Ok(
            await bus.InvokeAsync<WorkflowStatusesResponse>(
                new ListWorkflowStatusesQuery(tenantId, includeArchived),
                ct
            )
        );
    }

    [HttpPost]
    [HasPermission(TasksPermissions.StatusesManage)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<WorkflowStatusResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateWorkflowStatusRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<WorkflowStatusResponse>>(
            new CreateWorkflowStatusCommand(
                tenantId,
                userId,
                request.DisplayName,
                request.Color,
                request.Category,
                request.Description
            ),
            ct
        );
        return result.IsSuccess
            ? CreatedAtAction(nameof(List), null, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{code}")]
    [HasPermission(TasksPermissions.StatusesManage)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<WorkflowStatusResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(string code, UpdateWorkflowStatusRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<WorkflowStatusResponse>>(
            new UpdateWorkflowStatusCommand(
                tenantId,
                userId,
                code,
                request.DisplayName,
                request.Color,
                request.Description,
                request.RowVersion
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("order")]
    [HasPermission(TasksPermissions.StatusesManage)]
    [RateLimit("task.g.update")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reorder(ReorderWorkflowStatusesRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new ReorderWorkflowStatusesCommand(tenantId, request.Category, request.Codes),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{code}/default")]
    [HasPermission(TasksPermissions.StatusesManage)]
    [RateLimit("task.g.update")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetDefault(string code, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new SetDefaultWorkflowStatusCommand(tenantId, userId, code), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{code}/archive")]
    [HasPermission(TasksPermissions.StatusesManage)]
    [RateLimit("task.g.update")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(
        string code,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ArchiveWorkflowStatusRequest? request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new ArchiveWorkflowStatusCommand(tenantId, userId, code, request?.MoveOpenTasksTo),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{code}/restore")]
    [HasPermission(TasksPermissions.StatusesManage)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<WorkflowStatusResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Restore(string code, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<WorkflowStatusResponse>>(
            new RestoreWorkflowStatusCommand(tenantId, userId, code),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
