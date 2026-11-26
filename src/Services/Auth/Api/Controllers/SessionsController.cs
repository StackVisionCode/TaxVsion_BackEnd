using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Sessions.Commands;
using TaxVision.Auth.Application.Sessions.Queries;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

[ApiController]
[Route("auth/sessions")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.CustomerPortal, ActorType.PlatformAdmin)]
public sealed class SessionsController(IMessageBus bus, IUserPermissionsSource permissionsSource) : ControllerBase
{
    [HttpGet("me")]
    [RateLimit("auth.f.session_read")]
    [ProducesResponseType<IReadOnlyList<SessionResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MySessions(CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<SessionResponse>>>(
            new GetMySessionsQuery(userId, CurrentSessionId()),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Sesiones activas de un usuario del tenant (requiere users.manage).</summary>
    [HttpGet("users/{targetUserId:guid}")]
    [HasPermission(PermissionCatalog.UsersManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("auth.f.session_read")]
    [ProducesResponseType<IReadOnlyList<SessionResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UserSessions(Guid targetUserId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<SessionResponse>>>(
            new GetUserSessionsQuery(
                tenantId,
                targetUserId,
                CallerActorType(),
                CurrentSessionId(),
                await CanSeeLocationAsync(ct),
                User.TryGetUserId(out var userId) ? userId : null
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Revoca una sesión (propia o, con users.manage, de otro usuario del tenant).</summary>
    [HttpDelete("{sessionId:guid}")]
    [RateLimit("auth.g.session_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new RevokeSessionCommand(
                userId,
                tenantId,
                sessionId,
                CanManageOthers: await permissionsSource.HasPermissionAsync(User, PermissionCatalog.UsersManage, ct),
                RequestingSessionId: CurrentSessionId(),
                RequestingActorType: CallerActorType()
            ),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>"Cerrar sesión en todos los dispositivos" (conserva la sesión actual).</summary>
    [HttpDelete]
    [RateLimit("auth.g.session_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeAllMySessions([FromQuery] bool includeCurrent, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        Guid? exceptSessionId = null;
        if (!includeCurrent && User.TryGetSessionId(out var currentSessionId))
            exceptSessionId = currentSessionId;

        var result = await bus.InvokeAsync<Result>(
            new RevokeAllMySessionsCommand(userId, tenantId, exceptSessionId),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Sesiones del personal de la oficina, por fecha de inicio (por defecto la última semana, máximo 90 días).</summary>
    [HttpGet]
    [HasPermission(PermissionCatalog.UsersManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("auth.f.session_read")]
    [ProducesResponseType<SessionPageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TenantSessions(
        [FromQuery] Guid? userId,
        [FromQuery] SessionStatus? status,
        [FromQuery] SessionChannel? channel,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        Response.Headers.CacheControl = "no-store";
        var result = await bus.InvokeAsync<Result<SessionPageResponse>>(
            new GetTenantSessionsQuery(
                tenantId,
                CallerActorType(),
                CurrentSessionId(),
                userId,
                status,
                channel,
                ToUtc(from),
                ToUtc(to),
                search,
                page,
                pageSize,
                await CanSeeLocationAsync(ct),
                User.TryGetUserId(out var requesterId) ? requesterId : null
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Conteos de la oficina en el periodo.</summary>
    [HttpGet("summary")]
    [HasPermission(PermissionCatalog.UsersManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("auth.f.session_read")]
    [ProducesResponseType<SessionSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        Response.Headers.CacheControl = "no-store";
        var result = await bus.InvokeAsync<Result<SessionSummaryResponse>>(
            new GetSessionSummaryQuery(
                tenantId,
                CallerActorType(),
                ToUtc(from),
                ToUtc(to),
                await CanSeeLocationAsync(ct)
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Cierra todas las sesiones de un miembro del personal; opcionalmente olvida sus dispositivos de confianza.</summary>
    [HttpDelete("users/{targetUserId:guid}")]
    [HasPermission(PermissionCatalog.UsersManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("auth.g.session_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeUserSessions(
        Guid targetUserId,
        [FromQuery] bool forgetTrustedDevices,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new RevokeUserSessionsCommand(userId, tenantId, CallerActorType(), targetUserId, forgetTrustedDevices),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private Task<bool> CanSeeLocationAsync(CancellationToken ct) =>
        permissionsSource.HasPermissionAsync(User, PermissionCatalog.SessionsLocationView, ct);

    private Guid? CurrentSessionId() => User.TryGetSessionId(out var sessionId) ? sessionId : null;

    /// <summary>Tipo de actor del token en el enum de Auth; nulo si falta (las reglas lo tratan como "no admin").</summary>
    private UserActorType? CallerActorType() =>
        User.GetActorType() switch
        {
            ActorType.TenantEmployee => UserActorType.TenantEmployee,
            ActorType.TenantAdmin => UserActorType.TenantAdmin,
            ActorType.CustomerPortal => UserActorType.CustomerPortal,
            ActorType.PlatformAdmin => UserActorType.PlatformAdmin,
            _ => null,
        };

    private static DateTime? ToUtc(DateTime? value) =>
        value switch
        {
            null => null,
            { Kind: DateTimeKind.Utc } utc => utc,
            { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
            { } unspecified => DateTime.SpecifyKind(unspecified, DateTimeKind.Utc),
        };
}
