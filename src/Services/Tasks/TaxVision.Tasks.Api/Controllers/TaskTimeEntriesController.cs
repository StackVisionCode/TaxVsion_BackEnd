using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Requests;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Timers;
using TaxVision.Tasks.Application.Timers.Commands;
using TaxVision.Tasks.Application.Timers.Queries;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers;

/// <summary>
/// Tiempo a mano, su corrección y la estimación. Anotar no exige ser responsable de la tarea; corregir o borrar
/// exige ser el autor o supervisar la oficina (<c>tasks.manage_all</c>). La duración viaja en minutos enteros.
/// </summary>
[ApiController]
[Route("tasks")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class TaskTimeEntriesController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    [HttpPost("{id:guid}/time-entries")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskTimerResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Log(Guid id, LogTimeRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskTimerResponse>>(
            new LogTaskTimeCommand(
                tenantId,
                id,
                userId,
                request.WorkedOn,
                request.Minutes,
                request.IsBillable,
                request.Note
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}/time-entries/{entryId:guid}")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskTimerResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Edit(Guid id, Guid entryId, EditTimeEntryRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskTimerResponse>>(
            new EditTaskTimeEntryCommand(
                tenantId,
                id,
                entryId,
                userId,
                await HasManageAllAsync(ct),
                request.Minutes,
                request.IsBillable,
                request.Note
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{id:guid}/time-entries/{entryId:guid}")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(Guid id, Guid entryId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new RemoveTaskTimeEntryCommand(tenantId, id, entryId, userId, await HasManageAllAsync(ct)),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Cambia o quita la estimación (<c>estimatedMinutes: null</c>).</summary>
    [HttpPut("{id:guid}/estimate")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Estimate(Guid id, ChangeEstimateRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskResponse>>(
            new ChangeTaskEstimateCommand(tenantId, id, userId, await HasManageAllAsync(ct), request.EstimatedMinutes),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Mis temporizadores en marcha en cualquier tarea: el CRM avisa antes de iniciar otro.</summary>
    [HttpGet("timers/mine/running")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<IReadOnlyList<RunningTimerResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MineRunning(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        return Ok(
            await bus.InvokeAsync<IReadOnlyList<RunningTimerResponse>>(
                new ListMyRunningTimersQuery(tenantId, userId),
                ct
            )
        );
    }

    private Task<bool> HasManageAllAsync(CancellationToken ct) =>
        permissions.HasPermissionAsync(User, TasksPermissions.ManageAll, ct);
}
