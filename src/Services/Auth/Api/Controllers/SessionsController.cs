using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Sessions.Commands;
using TaxVision.Auth.Application.Sessions.Queries;
using TaxVision.Auth.Domain.Roles;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

[ApiController]
[Route("auth/sessions")]
[Authorize]
public sealed class SessionsController(IMessageBus bus) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType<IReadOnlyList<SessionResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MySessions(CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<SessionResponse>>>(
            new GetMySessionsQuery(userId), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Sesiones activas de un usuario del tenant (requiere users.manage).</summary>
    [HttpGet("users/{targetUserId:guid}")]
    [Authorization.HasPermission(PermissionCatalog.UsersManage)]
    [ProducesResponseType<IReadOnlyList<SessionResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UserSessions(
        Guid targetUserId,
        CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<SessionResponse>>>(
            new GetUserSessionsQuery(tenantId, targetUserId), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Revoca una sesión (propia o, con users.manage, de otro usuario del tenant).</summary>
    [HttpDelete("{sessionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeSession(
        Guid sessionId,
        CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new RevokeSessionCommand(
                userId,
                tenantId,
                sessionId,
                CanManageOthers: User.HasPermission(PermissionCatalog.UsersManage)),
            ct);

        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>"Cerrar sesión en todos los dispositivos" (conserva la sesión actual).</summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeAllMySessions(
        [FromQuery] bool includeCurrent,
        CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        Guid? exceptSessionId = null;
        if (!includeCurrent && User.TryGetSessionId(out var currentSessionId))
            exceptSessionId = currentSessionId;

        var result = await bus.InvokeAsync<Result>(
            new RevokeAllMySessionsCommand(userId, tenantId, exceptSessionId), ct);

        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
