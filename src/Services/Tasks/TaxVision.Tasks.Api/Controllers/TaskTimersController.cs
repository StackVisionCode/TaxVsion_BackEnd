using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Requests;
using TaxVision.Tasks.Application.Timers;
using TaxVision.Tasks.Application.Timers.Abstractions;
using TaxVision.Tasks.Application.Timers.Commands;
using TaxVision.Tasks.Application.Timers.Queries;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers;

/// <summary>
/// Los timers son opt-in: estos endpoints son el único camino que abre uno. Ni crear, ni asignar, ni
/// completar una tarea arranca un reloj.
/// </summary>
[ApiController]
[Route("tasks")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class TaskTimersController(IMessageBus bus) : ControllerBase
{
    /// <summary>Ventana por defecto del reporte cuando el llamador no manda rango.</summary>
    private const int DefaultReportDays = 30;

    [HttpPost("{id:guid}/timer/start")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskTimerResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Start(Guid id, StartTimerRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskTimerResponse>>(
            new StartTaskTimerCommand(tenantId, id, userId, request.IsBillable),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/timer/{timerId:guid}/stop")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskTimerResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Stop(Guid id, Guid timerId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskTimerResponse>>(
            new StopTaskTimerCommand(tenantId, id, timerId, userId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}/timers")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<IReadOnlyList<TaskTimerResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListForTask(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<TaskTimerResponse>>>(
            new ListTaskTimersQuery(tenantId, id),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Horas imputadas por tarea y persona. Sólo cuenta los tramos cerrados.</summary>
    [HttpGet("timers/report")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.h.search")]
    [ProducesResponseType<IReadOnlyList<TaskTimerReportRow>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Report(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] Guid? userId,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var to = toUtc ?? DateTime.UtcNow;
        var from = fromUtc ?? to.AddDays(-DefaultReportDays);

        var result = await bus.InvokeAsync<IReadOnlyList<TaskTimerReportRow>>(
            new GetTaskTimerReportQuery(tenantId, from, to, userId),
            ct
        );
        return Ok(result);
    }
}
