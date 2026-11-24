using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Requests;
using TaxVision.Tasks.Application.Series;
using TaxVision.Tasks.Application.Series.Commands;
using TaxVision.Tasks.Application.Series.Queries;
using TaxVision.Tasks.Domain.Series;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers;

/// <summary>
/// La regla de la recurrencia. Las ocurrencias se leen y se cierran por <c>/tasks</c> como cualquier
/// otra tarea: acá sólo se administra la serie.
/// </summary>
[ApiController]
[Route("tasks/series")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class TaskSeriesController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    [HttpPost]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.h.series_write")]
    [ProducesResponseType<TaskSeriesResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateTaskSeriesRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var command = new CreateTaskSeriesCommand(
            tenantId,
            userId,
            request.Title,
            request.Description,
            request.Priority,
            request.CustomerId,
            request.TaxYear,
            request.EstimatedHours,
            request.AssigneeUserId ?? userId,
            request.IsStatutory,
            request.Rule,
            request.TimeZoneId,
            request.Mode,
            request.AnchorUtc,
            request.EndsAtUtc,
            request.MaxOccurrences,
            request.AnchorOn,
            await CanViewAllCustomersAsync(ct)
        );

        var result = await bus.InvokeAsync<Result<TaskSeriesResponse>>(command, ct);
        return result.IsFailure
            ? StatusCode(result.Error.ToHttpStatusCode(), result.Error)
            : CreatedAtAction(nameof(GetById), new { seriesId = result.Value.Id }, result.Value);
    }

    /// <summary>
    /// Qué fechas produciría una serie, con el mismo motor que crea las tareas. Sin estado: el constructor lo pide al
    /// cambiar una opción. Una regla no válida vuelve con el motivo entre los avisos.
    /// </summary>
    [HttpPost("preview")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<TaskSeriesPreviewResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(PreviewTaskSeriesRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<TaskSeriesPreviewResponse>(
            new PreviewTaskSeriesQuery(
                tenantId,
                request.Rule,
                request.Mode,
                request.StartsOn,
                request.EndsOn,
                request.MaxOccurrences,
                request.Count ?? 5
            ),
            ct
        );
        return Ok(result);
    }

    [HttpGet("{seriesId:guid}")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<TaskSeriesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid seriesId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskSeriesResponse>>(
            new GetTaskSeriesByIdQuery(tenantId, seriesId, userId, await CanViewAllCustomersAsync(ct)),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpGet]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<TaskSeriesPage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] SeriesStatus? status,
        [FromQuery] bool mine = false,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<TaskSeriesPage>(
            new ListTaskSeriesQuery(tenantId, status, userId, await CanViewAllCustomersAsync(ct), mine, page, size),
            ct
        );
        return Ok(result);
    }

    /// <summary>Edita la serie: vale para las ocurrencias futuras y, si se pide, para la abierta.</summary>
    [HttpPut("{seriesId:guid}")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.h.series_write")]
    [ProducesResponseType<TaskSeriesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid seriesId, UpdateTaskSeriesRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskSeriesResponse>>(
            new UpdateTaskSeriesCommand(
                tenantId,
                seriesId,
                userId,
                await HasManageAllAsync(ct),
                request.Title,
                request.Description,
                request.Priority,
                request.AssigneeUserId,
                request.IsStatutory,
                request.Rule,
                request.Mode,
                request.StartsOn,
                request.EndsOn,
                request.MaxOccurrences,
                request.ApplyToOpenInstance,
                request.CustomerId,
                request.RowVersion,
                await CanViewAllCustomersAsync(ct)
            ),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    /// <summary>Las tareas que generó la serie, la más reciente primero.</summary>
    [HttpGet("{seriesId:guid}/occurrences")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<IReadOnlyList<SeriesOccurrenceResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Occurrences(Guid seriesId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<SeriesOccurrenceResponse>>>(
            new ListSeriesOccurrencesQuery(tenantId, seriesId, userId, await CanViewAllCustomersAsync(ct)),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpPost("{seriesId:guid}/pause")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.h.series_write")]
    [ProducesResponseType<TaskSeriesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Pause(Guid seriesId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskSeriesResponse>>(
            new PauseTaskSeriesCommand(tenantId, seriesId, userId, await HasManageAllAsync(ct)),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpPost("{seriesId:guid}/resume")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.h.series_write")]
    [ProducesResponseType<TaskSeriesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Resume(Guid seriesId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskSeriesResponse>>(
            new ResumeTaskSeriesCommand(tenantId, seriesId, userId, await HasManageAllAsync(ct)),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpPost("{seriesId:guid}/end")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.h.series_write")]
    [ProducesResponseType<TaskSeriesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> End(Guid seriesId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskSeriesResponse>>(
            new EndTaskSeriesCommand(tenantId, seriesId, userId, await HasManageAllAsync(ct)),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    /// <summary>A1 — el override de supervisión, igual que en TasksController: quien lo tiene puede
    /// tocar lo de cualquiera dentro de su tenant.</summary>
    private Task<bool> CanViewAllCustomersAsync(CancellationToken ct) =>
        permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);

    private Task<bool> HasManageAllAsync(CancellationToken ct) =>
        permissions.HasPermissionAsync(User, TasksPermissions.ManageAll, ct);
}
