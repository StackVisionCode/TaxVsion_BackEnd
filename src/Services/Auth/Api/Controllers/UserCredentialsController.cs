using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using BuildingBlocks.Web.Security;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Domain.Roles;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// La oficina administra la credencial de un empleado (03 §14.1): ver el estado, enviar y cancelar el link de reset,
/// exigir el cambio de contraseña, asegurar la cuenta, resetear el MFA y corregir el correo.
/// Solo TenantAdmin; el PlatformAdmin pasa el filtro pero lo excluye la política (y vive en otro tenant).
/// Las mutaciones exigen reautenticación reciente. Ninguna respuesta lleva token, link ni correo.
/// </summary>
[ApiController]
[Route("auth/users/{userId:guid}")]
[AllowActorTypes(ActorType.TenantAdmin)]
public sealed class UserCredentialsController(IMessageBus bus) : ControllerBase
{
    [HttpGet("credentials")]
    [HasPermission(PermissionCatalog.UsersPasswordSendReset)]
    [RateLimit("auth.f.user_read")]
    [ProducesResponseType<StaffCredentialStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid userId, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var actorId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<StaffCredentialStatusResponse>>(
            new GetStaffCredentialStatusQuery(tenantId, actorId, userId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("credentials/reset-link")]
    [HasPermission(PermissionCatalog.UsersPasswordSendReset)]
    [RequireRecentAuthentication]
    [RateLimit("auth.n.credential_admin")]
    [ProducesResponseType<ResetLinkIssuedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<Error>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<Error>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SendResetLink(Guid userId, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var actorId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ResetLinkIssuedResponse>>(
            new SendStaffPasswordResetLinkCommand(tenantId, actorId, userId, ReauthenticatedAt()),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("credentials/reset-link")]
    [HasPermission(PermissionCatalog.UsersPasswordSendReset)]
    [RequireRecentAuthentication]
    [RateLimit("auth.n.credential_admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<Error>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelResetLink(Guid userId, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var actorId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new CancelStaffPasswordResetLinkCommand(tenantId, actorId, userId, ReauthenticatedAt()),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Cambio obligatorio en el próximo login (03 §3 fila 2). Repetido: 200 con <c>alreadyInState</c>.</summary>
    [HttpPost("credentials/require-change")]
    [HasPermission(PermissionCatalog.UsersPasswordRequireChange)]
    [RequireRecentAuthentication]
    [RateLimit("auth.n.credential_admin")]
    [ProducesResponseType<CredentialStateChangedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<Error>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequireChange(
        Guid userId,
        [FromBody] CredentialAdministrationRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var actorId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CredentialStateChangedResponse>>(
            new RequireStaffPasswordChangeCommand(
                tenantId,
                actorId,
                userId,
                request.ReasonCode,
                request.ReasonNote,
                request.IdentityConfirmed,
                ReauthenticatedAt()
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Asegura la cuenta y envía el link seguro (03 §3 fila 3). De nuevo = reenviar el link seguro (AJ-15).</summary>
    [HttpPost("credentials/secure")]
    [HasPermission(PermissionCatalog.UsersCredentialsSecure)]
    [RequireRecentAuthentication]
    [RateLimit("auth.n.credential_admin")]
    [ProducesResponseType<AccountSecuredResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<Error>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<Error>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Secure(
        Guid userId,
        [FromBody] CredentialAdministrationRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var actorId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<AccountSecuredResponse>>(
            new SecureStaffAccountCommand(
                tenantId,
                actorId,
                userId,
                request.ReasonCode,
                request.ReasonNote,
                request.IdentityConfirmed,
                ReauthenticatedAt()
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Quita la verificación en dos pasos tras confirmar la identidad fuera del sistema (03 §3 fila 4). Sin factores:
    /// 200 con <c>NotEnabled</c>. Motivo obligatorio; <c>identityConfirmed</c> queda en la auditoría.
    /// </summary>
    [HttpPost("mfa/reset")]
    [HasPermission(PermissionCatalog.UsersMfaReset)]
    [RequireRecentAuthentication]
    [RateLimit("auth.n.credential_admin")]
    [ProducesResponseType<StaffMfaResetResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<Error>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResetMfa(
        Guid userId,
        [FromBody] CredentialAdministrationRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var actorId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<StaffMfaResetResponse>>(
            new ResetStaffMfaCommand(
                tenantId,
                actorId,
                userId,
                request.ReasonCode,
                request.ReasonNote,
                request.IdentityConfirmed,
                ReauthenticatedAt()
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record ChangeEmailRequest(string NewEmail);

    /// <summary>
    /// El Owner corrige el correo laboral de un empleado (D-11): link al correo nuevo y aviso al anterior. El correo de
    /// login no cambia hasta que el nuevo se confirma. Ningún link de reset sale en el mismo paso.
    /// </summary>
    [HttpPost("email/change-request")]
    [HasPermission(PermissionCatalog.UsersManage)]
    [RequireRecentAuthentication]
    [RateLimit("auth.n.credential_admin")]
    [ProducesResponseType<StaffEmailChangeRequestedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<Error>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestEmailChange(
        Guid userId,
        [FromBody] ChangeEmailRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var actorId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<StaffEmailChangeRequestedResponse>>(
            new RequestStaffEmailChangeCommand(tenantId, actorId, userId, request.NewEmail, ReauthenticatedAt()),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private DateTime? ReauthenticatedAt() =>
        User.TryGetReauthenticatedAt(out var reauthenticatedAt) ? reauthenticatedAt.UtcDateTime : null;
}
