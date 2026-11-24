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
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Queries;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers;

/// <summary>
/// Tablero por columnas: una por estado activo de la oficina, con su total real y 20 tarjetas. Las columnas
/// ocultas o contraídas solo piden su total (<c>expanded</c>). Por defecto solo tareas principales (D-8).
/// </summary>
[ApiController]
[Route("tasks/board/columns")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class TaskBoardController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    [HttpGet]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.h.search")]
    [ProducesResponseType<TaskBoardColumnsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Columns([FromQuery] TaskBoardRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var expanded = request.Expanded is null
            ? null
            : (IReadOnlySet<string>)
                request
                    .Expanded.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet();
        var result = await bus.InvokeAsync<TaskBoardColumnsResponse>(
            new GetTaskBoardColumnsQuery(
                tenantId,
                request.ToFilter(),
                expanded,
                request.Take ?? 20,
                request.Sort ?? TaskBoardSort.Due,
                userId,
                canViewAll,
                request.Due,
                DateTime.UtcNow
            ),
            ct
        );
        return Ok(result);
    }

    /// <summary>"Load more": <c>cursor</c> es el que devolvió la columna.</summary>
    [HttpGet("{code}")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.h.search")]
    [ProducesResponseType<TaskBoardColumnSlice>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Column(
        string code,
        [FromQuery] TaskBoardRequest request,
        [FromQuery] string? cursor,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var skip = int.TryParse(cursor, out var parsed) ? parsed : 0;
        var result = await bus.InvokeAsync<Result<TaskBoardColumnSlice>>(
            new GetTaskBoardColumnQuery(
                tenantId,
                request.ToFilter(),
                code,
                skip,
                request.Take ?? 20,
                request.Sort ?? TaskBoardSort.Due,
                userId,
                canViewAll,
                request.Due,
                DateTime.UtcNow
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
