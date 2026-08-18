using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Requests;
using TaxVision.Tasks.Application.Labels;
using TaxVision.Tasks.Application.Labels.Commands;
using TaxVision.Tasks.Application.Labels.Queries;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers;

/// <summary>
/// Catálogo de presentación de la firma. Editarlo no cambia el motor: las reglas leen
/// <c>TaskItemStatus</c>, no el nombre que el tenant le puso.
/// </summary>
[ApiController]
[Route("tasks")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class TaskLabelsController(IMessageBus bus) : ControllerBase
{
    [HttpGet("taxonomies")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<TaskTaxonomiesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Taxonomies(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<TaskTaxonomiesResponse>(new GetTaskTaxonomiesQuery(tenantId), ct);
        return Ok(result);
    }

    [HttpPost("labels")]
    [HasPermission(TasksPermissions.TemplatesManage)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskLabelResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(UpsertTaskLabelRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskLabelResponse>>(
            new CreateTaskLabelCommand(
                tenantId,
                request.Code,
                request.DisplayName,
                request.Color,
                request.MapsToStatus,
                request.SortOrder
            ),
            ct
        );

        return result.IsSuccess
            ? CreatedAtAction(nameof(Taxonomies), null, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("labels/{id:guid}")]
    [HasPermission(TasksPermissions.TemplatesManage)]
    [RateLimit("task.g.update")]
    [ProducesResponseType<TaskLabelResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, UpsertTaskLabelRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TaskLabelResponse>>(
            new UpdateTaskLabelCommand(
                tenantId,
                id,
                request.DisplayName,
                request.Color,
                request.MapsToStatus,
                request.SortOrder
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("labels/{id:guid}")]
    [HasPermission(TasksPermissions.TemplatesManage)]
    [RateLimit("task.g.update")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new DeleteTaskLabelCommand(tenantId, id), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
