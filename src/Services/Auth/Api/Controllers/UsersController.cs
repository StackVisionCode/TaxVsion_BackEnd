using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Permissions.Commands;
using TaxVision.Auth.Application.Permissions.Queries;
using TaxVision.Auth.Application.Tenants.Queries;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Application.Users.Queries;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

[ApiController]
[Route("auth/users")]
public sealed class UsersController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCatalog.UsersView)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("auth.h.user_search")]
    [ProducesResponseType<PagedResult<UserSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] Guid? customerId = null,
        [FromQuery] UserAccountKind? accountKind = null,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<PagedResult<UserSummaryResponse>>>(
            new GetUsersQuery(tenantId, page, size, search, isActive, customerId, accountKind),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{userId:guid}")]
    [HasPermission(PermissionCatalog.UsersView)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("auth.f.user_read")]
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
    [RateLimit("auth.g.user_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid userId, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var requesterId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new DeactivateUserCommand(tenantId, userId, requesterId, CallerActorType()),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPatch("{userId:guid}/reactivate")]
    [HasPermission(PermissionCatalog.UsersManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("auth.g.user_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reactivate(Guid userId, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var requesterId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new ReactivateUserCommand(tenantId, userId, requesterId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record OffboardUserRequest(Guid? SuccessorUserId);

    // Retirar del tenant (offboard): terminal. Más estricto que deactivate — solo admins.
    [HttpPost("{userId:guid}/offboard")]
    [HasPermission(PermissionCatalog.UsersManage)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("auth.g.user_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Offboard(
        Guid userId,
        [FromBody] OffboardUserRequest? request,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var requesterId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new OffboardUserCommand(tenantId, userId, requesterId, request?.SuccessorUserId),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record AssignRolesRequest(IReadOnlyList<Guid> RoleIds);

    [HttpPut("{userId:guid}/roles")]
    [HasPermission(PermissionCatalog.RolesManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("auth.g.user_manage")]
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

    /// <summary>The target seat's actor type, active roles and role-granted permissions (grouped by
    /// module, each flagged if currently denied) plus the permission version — everything the "Edit
    /// access" drawer needs to render its toggles in one call. Managing access is gated by roles.manage.</summary>
    [HttpGet("{userId:guid}/effective-access")]
    [HasPermission(PermissionCatalog.RolesManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("auth.f.user_read")]
    [ProducesResponseType<UserEffectiveAccessResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEffectiveAccess(Guid userId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<UserEffectiveAccessResponse>>(
            new GetUserEffectiveAccessQuery(tenantId, userId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Un deny con su razón y su expiración. Ambas opcionales; sin expiración es indefinido.</summary>
    public sealed record PermissionDenyRequest(Guid PermissionId, string? Reason, DateTime? ExpiresAtUtc);

    /// <summary>
    /// Dos formas del mismo set, por compatibilidad: <c>deniedPermissionIds</c> es el contrato que ya
    /// consume el CRM desplegado; <c>denies</c> es el que además lleva razón y expiración. Si viene
    /// <c>denies</c>, manda ese.
    /// </summary>
    public sealed record SetPermissionOverridesRequest(
        IReadOnlyList<Guid>? DeniedPermissionIds,
        IReadOnlyList<PermissionDenyRequest>? Denies
    );

    /// <summary>Replaces the target user's per-user deny set (the RBAC deny layer). Deny-only: to grant a
    /// permission you assign a role. Idempotent — the given set fully replaces the previous one (an empty
    /// set removes every override). All guardrails (anti self-lockout, tenant isolation, actor-type
    /// coherence) run in the handler. Gated by roles.manage.</summary>
    [HttpPut("{userId:guid}/permission-overrides")]
    [HasPermission(PermissionCatalog.RolesManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("auth.g.user_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPermissionOverrides(
        Guid userId,
        SetPermissionOverridesRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var requesterId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var denies = request.Denies is { Count: > 0 }
            ? request
                .Denies.Select(deny => new PermissionDenyInput(deny.PermissionId, deny.Reason, deny.ExpiresAtUtc))
                .ToList()
            : (request.DeniedPermissionIds ?? []).Select(id => new PermissionDenyInput(id)).ToList();

        var result = await bus.InvokeAsync<Result>(
            new SetUserPermissionOverridesCommand(tenantId, userId, denies, requesterId),
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
    [RateLimit("auth.g.user_profile_manage")]
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
    // El Account del Landing lo compone con su read model: los asientos usados solo los sabe Auth.
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("auth.f.tenant_limits_read")]
    [ProducesResponseType<TenantLimitsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTenantLimits(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantLimitsResponse>>(new GetTenantLimitsQuery(tenantId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Actor type del caller tal como lo trae el JWT, traducido al enum de Auth. <c>null</c> si el
    /// claim falta o no matchea (fail-closed: los guards de jerarquía tratan null como "no admin").
    /// </summary>
    private UserActorType? CallerActorType() =>
        User.GetActorType() switch
        {
            ActorType.TenantEmployee => UserActorType.TenantEmployee,
            ActorType.TenantAdmin => UserActorType.TenantAdmin,
            ActorType.CustomerPortal => UserActorType.CustomerPortal,
            ActorType.PlatformAdmin => UserActorType.PlatformAdmin,
            _ => null,
        };
}
