using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Application.Tasks.Queries;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers;

/// <summary>Papelera de tareas (D-25): lo borrado vuelve con Restore durante 30 días.</summary>
[ApiController]
[Route("tasks")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class TaskTrashController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    [HttpGet("trash")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<TaskTrashPage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int? page, [FromQuery] int? size, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<TaskTrashPage>(
            new ListTaskTrashQuery(tenantId, userId, canViewAll, page ?? 1, size ?? 20),
            ct
        );
        return Ok(result);
    }

    [HttpPost("{id:guid}/restore")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.g.update")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var manageAll = await permissions.HasPermissionAsync(User, TasksPermissions.ManageAll, ct);
        var result = await bus.InvokeAsync<Result>(new RestoreTaskCommand(tenantId, id, userId, manageAll), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
