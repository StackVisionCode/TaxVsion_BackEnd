using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Requests;
using TaxVision.Tasks.Application.Dependencies.Commands;
using TaxVision.Tasks.Application.Dependencies.Queries;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Application.Tasks.Queries;
using TaxVision.Tasks.Domain.Tasks;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers;

/// <summary>
/// Sólo staff: una tarea es trabajo interno de la firma y el cliente final no ve la lista. Lo que le
/// llega sale por Notification, no por acá.
/// </summary>
[ApiController]
[Route("tasks")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class TasksController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    private const int DefaultSize = 20;
    private const int BoardTake = 500;
    private const int CalendarTake = 500;

    [HttpPost]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.create")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateTaskRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new CreateTaskCommand(
                tenantId,
                userId,
                request.Title,
                request.Description,
                request.Priority,
                request.AssigneeUserId,
                request.CustomerId,
                request.TaxYear,
                request.DueAtUtc,
                request.DueTimeZoneId,
                request.DueIsStatutory,
                request.EstimatedHours
            ),
            ct
        );

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/subtasks")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.create")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateSubtask(Guid id, CreateSubtaskRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new CreateSubtaskCommand(
                tenantId,
                id,
                userId,
                await HasManageAllAsync(ct),
                request.Title,
                request.Description,
                request.Priority,
                request.AssigneeUserId,
                request.DueAtUtc,
                request.DueTimeZoneId,
                request.DueIsStatutory,
                request.EstimatedHours
            ),
            ct
        );

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskResponse>>(new GetTaskByIdQuery(tenantId, id), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}/subtasks")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<PagedResult<TaskResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSubtasks(
        Guid id,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<PagedResult<TaskResponse>>(
            new ListSubtasksQuery(tenantId, id, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return Ok(result);
    }

    [HttpGet("mine")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<PagedResult<TaskResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine(
        [FromQuery] TaskItemStatus? status,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<PagedResult<TaskResponse>>(
            new ListMyTasksQuery(tenantId, userId, status, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return Ok(result);
    }

    [HttpGet("by-customer/{customerId:guid}")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<PagedResult<TaskResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ByCustomer(
        Guid customerId,
        [FromQuery] int? taxYear,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<PagedResult<TaskResponse>>(
            new ListTasksByCustomerQuery(tenantId, customerId, taxYear, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return Ok(result);
    }

    [HttpGet("waiting-on-client")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.waiting_on_client")]
    [ProducesResponseType<PagedResult<TaskResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> WaitingOnClient([FromQuery] int page, [FromQuery] int size, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<PagedResult<TaskResponse>>(
            new ListWaitingOnClientTasksQuery(tenantId, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return Ok(result);
    }

    [HttpGet("search")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.h.search")]
    [ProducesResponseType<PagedResult<TaskResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] TaskItemStatus? status,
        [FromQuery] Guid? assigneeUserId,
        [FromQuery] Guid? customerId,
        [FromQuery] int? taxYear,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var filter = new TaskQueryFilter(q, status, assigneeUserId, customerId, taxYear);
        var result = await bus.InvokeAsync<PagedResult<TaskResponse>>(
            new SearchTasksQuery(tenantId, filter, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return Ok(result);
    }

    /// <summary>Tablero y calendario salen de la misma tabla; sólo cambia la forma de salida.</summary>
    [HttpGet("board")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.h.search")]
    [ProducesResponseType<TaskBoardResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Board(
        [FromQuery] Guid? assigneeUserId,
        [FromQuery] Guid? customerId,
        [FromQuery] int? taxYear,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var filter = new TaskQueryFilter(null, null, assigneeUserId, customerId, taxYear, OnlyOpen: true);
        var result = await bus.InvokeAsync<TaskBoardResponse>(new GetTaskBoardQuery(tenantId, filter, BoardTake), ct);
        return Ok(result);
    }

    [HttpGet("calendar")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.h.search")]
    [ProducesResponseType<IReadOnlyList<TaskCalendarEntry>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Calendar(
        [FromQuery] DateTime fromUtc,
        [FromQuery] DateTime toUtc,
        [FromQuery] Guid? assigneeUserId,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<IReadOnlyList<TaskCalendarEntry>>(
            new GetTaskCalendarQuery(tenantId, fromUtc, toUtc, assigneeUserId, CalendarTake),
            ct
        );
        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, UpdateTaskDetailsRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new UpdateTaskDetailsCommand(
                tenantId,
                id,
                userId,
                await HasManageAllAsync(ct),
                request.Title,
                request.Description
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}/priority")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangePriority(Guid id, ChangeTaskPriorityRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new ChangeTaskPriorityCommand(tenantId, id, userId, await HasManageAllAsync(ct), request.Priority),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}/due")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeDue(Guid id, ChangeTaskDueRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new ChangeTaskDueCommand(
                tenantId,
                id,
                userId,
                await HasManageAllAsync(ct),
                request.DueAtUtc,
                request.TimeZoneId,
                request.IsStatutory,
                request.StatutoryChangeReason
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/start")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new StartTaskCommand(tenantId, id, userId, await HasManageAllAsync(ct)),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/complete")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new CompleteTaskCommand(tenantId, id, userId, await HasManageAllAsync(ct)),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/reopen")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new ReopenTaskCommand(tenantId, id, userId, await HasManageAllAsync(ct)),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid id, CancelTaskRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new CancelTaskCommand(tenantId, id, userId, await HasManageAllAsync(ct), request.Reason),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Sin restricción de dirección: <c>tasks.assign</c> alcanza para que un empleado le asigne a un
    /// admin. El flujo de revisión interna lo exige.
    /// </summary>
    [HttpPut("{id:guid}/assignee")]
    [HasPermission(TasksPermissions.Assign)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Assign(Guid id, AssignTaskRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new AssignTaskCommand(tenantId, id, request.AssigneeUserId, userId, await HasManageAllAsync(ct)),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{id:guid}/assignee")]
    [HasPermission(TasksPermissions.Assign)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Unassign(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new UnassignTaskCommand(tenantId, id, userId, await HasManageAllAsync(ct)),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new DeleteTaskCommand(tenantId, id, userId, await HasManageAllAsync(ct)),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/wait-on-client")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.wait_on_client")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> WaitOnClient(Guid id, WaitOnClientRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new MoveTaskToWaitingOnClientCommand(
                tenantId,
                id,
                userId,
                await HasManageAllAsync(ct),
                request.ExpectedItems,
                request.ClientDueAtUtc
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/dependencies")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.dependency")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AddDependency(Guid id, AddDependencyRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new AddDependencyCommand(tenantId, id, request.DependsOnTaskId, userId),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{id:guid}/dependencies/{dependsOnTaskId:guid}")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.dependency")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveDependency(Guid id, Guid dependsOnTaskId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new RemoveDependencyCommand(tenantId, id, dependsOnTaskId), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}/graph")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.h.graph")]
    [ProducesResponseType<TaskDependencyGraphResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Graph(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<TaskDependencyGraphResponse>(
            new GetTaskDependencyGraphQuery(tenantId, id),
            ct
        );
        return Ok(result);
    }

    /// <summary>Override de supervisión: mover la tarea de otro. Se resuelve por proyección, no por el JWT.</summary>
    private Task<bool> HasManageAllAsync(CancellationToken ct) =>
        permissions.HasPermissionAsync(User, TasksPermissions.ManageAll, ct);

    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private static int NormalizeSize(int size) => size is < 1 or > 100 ? DefaultSize : size;
}
