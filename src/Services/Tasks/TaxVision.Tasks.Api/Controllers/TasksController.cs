using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TaxVision.Tasks.Api.Requests;
using TaxVision.Tasks.Api.Responses;
using TaxVision.Tasks.Application.Dependencies.Commands;
using TaxVision.Tasks.Application.Dependencies.Queries;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Application.Tasks.Queries;
using TaxVision.Tasks.ClientForms.Application.Offboarding;
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
                request.EstimatedHours,
                request.DueOn,
                await CanAssignAsync(ct),
                await CanViewAllCustomersAsync(ct)
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
                request.EstimatedHours,
                request.DueOn,
                await CanAssignAsync(ct)
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
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new GetTaskByIdQuery(tenantId, id, userId, canViewAll),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- GET /tasks/offboarding-impact/{userId} ----------
    // Pre-flight (punto 3.2): lo que se reasigna o cierra en Tasks y Client Forms al retirar a este empleado.
    [HttpGet("offboarding-impact/{userId:guid}")]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [HasPermission(UserManagementPermissions.UsersManage)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<TasksOffboardingImpactResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> OffboardingImpact(Guid userId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var tasks = await bus.InvokeAsync<OffboardingImpactResponse>(new OffboardingImpactQuery(tenantId, userId), ct);
        var forms = await bus.InvokeAsync<ClientFormsOffboardingImpactResponse>(
            new ClientFormsOffboardingImpactQuery(tenantId, userId),
            ct
        );
        return Ok(
            new TasksOffboardingImpactResponse(
                tasks.OpenTasks,
                tasks.OpenClientRequests,
                tasks.RunningTimers,
                forms.IntakeLinks,
                forms.OpenFormRequests
            )
        );
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
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<PagedResult<TaskResponse>>(
            new ListSubtasksQuery(tenantId, id, NormalizePage(page), NormalizeSize(size), userId, canViewAll),
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
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<PagedResult<TaskResponse>>(
            new ListTasksByCustomerQuery(
                tenantId,
                customerId,
                taxYear,
                NormalizePage(page),
                NormalizeSize(size),
                userId,
                canViewAll
            ),
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
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<PagedResult<TaskResponse>>(
            new ListWaitingOnClientTasksQuery(tenantId, NormalizePage(page), NormalizeSize(size), userId, canViewAll),
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
        [FromQuery] TaskDueBucket? due,
        [FromQuery] string? statusCode,
        [FromQuery] TaskPriority? priority,
        [FromQuery] bool rootOnly,
        [FromQuery] bool withMySubtasks,
        [FromQuery] bool openOnly,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var filter = new TaskQueryFilter(
            q,
            status,
            assigneeUserId,
            customerId,
            taxYear,
            openOnly,
            Priority: priority,
            RootOnly: rootOnly,
            AlsoSubtasksOf: withMySubtasks ? userId : null
        );
        var (pageNumber, pageSize) = (NormalizePage(page), NormalizeSize(size));
        var result = await bus.InvokeAsync<PagedResult<TaskResponse>>(
            new SearchTasksQuery(
                tenantId,
                filter,
                pageNumber,
                pageSize,
                userId,
                canViewAll,
                due,
                DateTime.UtcNow,
                statusCode
            ),
            ct
        );
        return Ok(result);
    }

    /// <summary>
    /// Conteos del dashboard por el día civil de la oficina, con la misma visibilidad que el tablero. <c>scope</c>:
    /// vacío = como siempre; <c>me</c> = asignadas a quien llama; <c>office</c> = toda la oficina (users.work.view).
    /// </summary>
    [HttpGet("summary")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<TaskSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Summary(
        [FromQuery] Guid? assigneeUserId,
        [FromQuery] Guid? customerId,
        [FromQuery] int? taxYear,
        [FromQuery] string? scope,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();
        if (!DashboardScopes.TryParse(scope, out var parsed))
            return StatusCode(StatusCodes.Status400BadRequest, DashboardScopes.Invalid);

        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        // El permiso de oficina sale del token; el handler decide con él.
        var canViewOffice =
            parsed == DashboardScope.Office
            && await permissions.HasPermissionAsync(User, UserManagementPermissions.UsersWorkView, ct);
        var result = await bus.InvokeAsync<Result<TaskSummaryResponse>>(
            new GetTaskSummaryQuery(
                tenantId,
                userId,
                canViewAll,
                assigneeUserId,
                customerId,
                taxYear,
                DateTime.UtcNow,
                parsed,
                canViewOffice
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("calendar")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.h.search")]
    [ProducesResponseType<IReadOnlyList<TaskCalendarEntry>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Calendar(
        [FromQuery] DateTime fromUtc,
        [FromQuery] DateTime toUtc,
        [FromQuery] Guid? assigneeUserId,
        [FromQuery] Guid? customerId,
        [FromQuery] int? taxYear,
        [FromQuery] bool openOnly,
        [FromQuery] string? q,
        [FromQuery] TaskItemStatus? status,
        [FromQuery] TaskPriority? priority,
        [FromQuery] bool rootOnly,
        [FromQuery] TaskDueBucket? due,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        // Los mismos filtros que el tablero; el estado concreto de la oficina queda para la búsqueda.
        var filter = new TaskQueryFilter(
            q,
            status,
            assigneeUserId,
            customerId,
            taxYear,
            openOnly,
            Priority: priority,
            RootOnly: rootOnly
        );
        var result = await bus.InvokeAsync<Result<TaskCalendarResult>>(
            new GetTaskCalendarQuery(tenantId, fromUtc, toUtc, filter, CalendarTake, userId, canViewAll, due),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        // La respuesta sigue siendo la lista; el corte va en una cabecera para no romper a los clientes actuales.
        if (result.Value.Truncated)
            Response.Headers["X-Result-Truncated"] = "true";
        return Ok(result.Value.Entries);
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

    /// <summary>Todo el panel de edición en una transacción; 409 si la tarea cambió desde que se abrió.</summary>
    [HttpPatch("{id:guid}")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Patch(Guid id, UpdateTaskRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new UpdateTaskCommand(
                tenantId,
                id,
                userId,
                await HasManageAllAsync(ct),
                await permissions.HasPermissionAsync(User, TasksPermissions.Assign, ct),
                request.Title,
                request.Description,
                request.Priority,
                request.DueOn,
                request.IsStatutory,
                request.StatutoryChangeReason,
                request.AssigneeUserId,
                request.EstimatedMinutes,
                request.CustomerId,
                request.TaxYear,
                request.RowVersion,
                await CanViewAllCustomersAsync(ct)
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
                request.StatutoryChangeReason,
                request.DueOn
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

    /// <summary>Vuelve a Not Started (tablero). Una tarea cerrada se reabre antes con <c>/reopen</c>.</summary>
    [HttpPost("{id:guid}/not-started")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MoveToNotStarted(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new MoveTaskToNotStartedCommand(tenantId, id, userId, await HasManageAllAsync(ct)),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/complete")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Complete(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CompleteTaskRequest? request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var openSubtasks = request?.CompleteOpenSubtasks ?? false;
        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new CompleteTaskCommand(tenantId, id, userId, await HasManageAllAsync(ct), openSubtasks),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>El movimiento único: lleva la tarea a un estado de la oficina en una sola operación.</summary>
    [HttpPost("{id:guid}/status")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MoveToStatus(Guid id, MoveTaskToStatusRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new MoveTaskToStatusCommand(
                tenantId,
                id,
                userId,
                await HasManageAllAsync(ct),
                request.Status,
                request.WaitingOnClient?.ExpectedItems,
                request.WaitingOnClient?.ClientDueOn,
                request.CancelReason,
                request.CompleteOpenSubtasks
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/reopen")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reopen(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReopenTaskRequest? request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var reopenParent = request?.ReopenParent ?? false;
        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new ReopenTaskCommand(tenantId, id, userId, await HasManageAllAsync(ct), reopenParent),
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

    /// <summary>Tomar para uno mismo una tarea sin asignar de la bandeja común: basta con escribir tareas.</summary>
    [HttpPost("{id:guid}/take")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Take(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new TakeTaskCommand(tenantId, id, userId, canViewAll),
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
                request.ClientDueAtUtc,
                request.ClientDueOn
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
            new AddDependencyCommand(tenantId, id, request.DependsOnTaskId, userId, await HasManageAllAsync(ct)),
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
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new RemoveDependencyCommand(tenantId, id, dependsOnTaskId, userId, await HasManageAllAsync(ct)),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}/graph")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.h.graph")]
    [ProducesResponseType<TaskDependencyGraphResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Graph(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<Result<TaskDependencyGraphResponse>>(
            new GetTaskDependencyGraphQuery(tenantId, id, userId, canViewAll),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}/relations")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.h.graph")]
    [ProducesResponseType<TaskRelationsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Relations(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<Result<TaskRelationsResponse>>(
            new GetTaskRelationsQuery(tenantId, id, userId, canViewAll),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Override de supervisión: mover la tarea de otro. Se resuelve por proyección, no por el JWT.</summary>
    private Task<bool> HasManageAllAsync(CancellationToken ct) =>
        permissions.HasPermissionAsync(User, TasksPermissions.ManageAll, ct);

    private Task<bool> CanViewAllCustomersAsync(CancellationToken ct) =>
        permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);

    private Task<bool> CanAssignAsync(CancellationToken ct) =>
        permissions.HasPermissionAsync(User, TasksPermissions.Assign, ct);

    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private static int NormalizeSize(int size) => size is < 1 or > 100 ? DefaultSize : size;
}
