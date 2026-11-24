using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Application.Activity;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers;

/// <summary>El historial de una tarea: quién cambió qué y cuándo.</summary>
[ApiController]
[Route("tasks")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class TaskActivityController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    [HttpGet("{id:guid}/activity")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<TaskActivityPage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        Guid id,
        [FromQuery] DateTime? before,
        [FromQuery] int? take,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<Result<TaskActivityPage>>(
            new ListTaskActivityQuery(tenantId, id, userId, canViewAll, before?.ToUniversalTime(), take ?? 50),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
