using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Tenants.Queries;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Application.Users.Queries;
using TaxVision.Auth.Domain.Roles;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

[ApiController]
[Route("auth/users")]
public sealed class UsersController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCatalog.UsersView)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType<PagedResult<UserSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<PagedResult<UserSummaryResponse>>>(
            new GetUsersQuery(tenantId, page, size, search, isActive),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{userId:guid}")]
    [HasPermission(PermissionCatalog.UsersView)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType<UserSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserById(Guid userId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<UserSummaryResponse>>(new GetUserByIdQuery(tenantId, userId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPatch("{userId:guid}/deactivate")]
    [HasPermission(PermissionCatalog.UsersManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid userId, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var requesterId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new DeactivateUserCommand(tenantId, userId, requesterId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPatch("{userId:guid}/reactivate")]
    [HasPermission(PermissionCatalog.UsersManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reactivate(Guid userId, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var requesterId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new ReactivateUserCommand(tenantId, userId, requesterId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record AssignRolesRequest(IReadOnlyList<Guid> RoleIds);

    [HttpPut("{userId:guid}/roles")]
    [HasPermission(PermissionCatalog.RolesManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AssignRoles(Guid userId, AssignRolesRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var requesterId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new AssignUserRolesCommand(tenantId, userId, request.RoleIds, requesterId),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record UpdateMyProfileRequest(string Name, string LastName, string? TimeZoneId);

    [HttpPut("me/profile")]
    [Authorize]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.CustomerPortal,
        ActorType.PlatformAdmin
    )]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateMyProfile(UpdateMyProfileRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new UpdateMyProfileCommand(userId, request.Name, request.LastName, request.TimeZoneId),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Plan, asientos usados/disponibles e invitaciones restantes del tenant.</summary>
    [HttpGet("/auth/tenants/limits")]
    [HasPermission(PermissionCatalog.UsersView)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType<TenantLimitsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTenantLimits(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantLimitsResponse>>(new GetTenantLimitsQuery(tenantId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
