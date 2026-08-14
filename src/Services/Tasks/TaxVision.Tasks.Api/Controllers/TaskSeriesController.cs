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
public sealed class TaskSeriesController(IMessageBus bus) : ControllerBase
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
            request.MaxOccurrences
        );

        var result = await bus.InvokeAsync<Result<TaskSeriesResponse>>(command, ct);
        return result.IsFailure
            ? StatusCode(result.Error.ToHttpStatusCode(), result.Error)
            : CreatedAtAction(nameof(GetById), new { seriesId = result.Value.Id }, result.Value);
    }

    [HttpGet("{seriesId:guid}")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<TaskSeriesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid seriesId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskSeriesResponse>>(
            new GetTaskSeriesByIdQuery(tenantId, seriesId),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpGet]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<IReadOnlyList<TaskSeriesResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] SeriesStatus? status, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<IReadOnlyList<TaskSeriesResponse>>(
            new ListTaskSeriesQuery(tenantId, status),
            ct
        );
        return Ok(result);
    }

    [HttpPost("{seriesId:guid}/pause")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.h.series_write")]
    [ProducesResponseType<TaskSeriesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Pause(Guid seriesId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskSeriesResponse>>(
            new PauseTaskSeriesCommand(tenantId, seriesId),
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
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskSeriesResponse>>(
            new ResumeTaskSeriesCommand(tenantId, seriesId),
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
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskSeriesResponse>>(
            new EndTaskSeriesCommand(tenantId, seriesId),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }
}
