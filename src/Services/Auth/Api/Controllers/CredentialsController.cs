using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Credentials.Commands;
using TaxVision.Auth.Application.Credentials.Queries;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class CredentialsController(IMessageBus bus) : ControllerBase
{
    /// <summary><c>AccountKind</c>: el CRM pide el reset de la cuenta Staff y el portal el de la Portal.</summary>
    public sealed record ForgotPasswordRequest(string Email, UserAccountKind? AccountKind = null);

    /// <summary>
    /// Solicita recuperación de contraseña. Desde el subdominio de una oficina solo resetea esa oficina; desde la
    /// entrada general, todas las del email. La oficina sale del Host, nunca del body. Siempre 202 (anti-enumeración).
    /// </summary>
    [HttpPost("password/forgot")]
    [AllowAnonymous]
    [RateLimitExempt(
        "Anónimo — protegido por ILoginThrottler.GetPasswordResetRetryAfterAsync (email+IP, Fase 18.1), un mecanismo de dominio separado del RateLimit HTTP."
    )]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequest request,
        [FromServices] IResolvedTenantContext tenantContext,
        CancellationToken ct
    )
    {
        await bus.InvokeAsync<Result>(
            new ForgotPasswordCommand(request.Email, request.AccountKind, tenantContext.ResolvedTenantId),
            ct
        );
        return Accepted();
    }

    /// <summary><c>Token</c>: el token crudo del enlace emailado. <c>NewPassword</c>: la contraseña nueva. El
    /// <c>HostTenantId</c> del command se setea aquí desde el <c>IResolvedTenantContext</c>, nunca del body.</summary>
    public sealed record ResetPasswordRequest(string Token, string NewPassword);

    [HttpPost("password/reset")]
    [AllowAnonymous]
    [RateLimitExempt(
        "Anónimo (token de reset por email) — máx. 5 intentos por token es la protección real (Fase 18.2), no requiere tenant/user JWT."
    )]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request,
        [FromServices] IResolvedTenantContext tenantContext,
        CancellationToken ct
    )
    {
        var command = new ResetPasswordCommand(request.Token, request.NewPassword, tenantContext.ResolvedTenantId);
        var result = await bus.InvokeAsync<Result>(command, ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>¿El enlace de reset todavía sirve y a qué oficina pertenece? 200 con <c>{status, tenant}</c>: la
    /// página consulta al abrirse para pintar el branding correcto y rebotar al subdominio del token si el link
    /// se abrió en otro host. No consume el token ni cuenta intentos. Espejo semántico de
    /// <c>GET /auth/invitations/validate</c>: anti-enumeración = siempre 200.</summary>
    [HttpGet("password/reset/validate")]
    [AllowAnonymous]
    [RateLimitExempt(
        "Anónimo (token de reset por email) — el token es imposible de adivinar y el Gateway acota la ruta por IP (PreAuthByIp)."
    )]
    [ProducesResponseType<PasswordResetValidationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateResetToken([FromQuery] string token, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<PasswordResetValidationResponse>>(
            new ValidatePasswordResetTokenQuery(token ?? string.Empty),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    [HttpPost("password/change")]
    [Authorize]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.CustomerPortal,
        ActorType.PlatformAdmin
    )]
    [RateLimit("auth.g.credentials_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetSessionId(out var sessionId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new ChangePasswordCommand(userId, sessionId, request.CurrentPassword, request.NewPassword),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record RequestEmailChangeRequest(string NewEmail);

    [HttpPost("me/email/change-request")]
    [Authorize]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.CustomerPortal,
        ActorType.PlatformAdmin
    )]
    [RateLimit("auth.g.credentials_manage")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestEmailChange(RequestEmailChangeRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new RequestEmailChangeCommand(userId, request.NewEmail), ct);

        return result.IsSuccess ? Accepted() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("me/email/confirm")]
    [AllowAnonymous]
    [RateLimitExempt(
        "Anónimo (token de un solo uso por email) — sin JWT que particionar; agregar protección HTTP nueva queda fuera de alcance de esta migración."
    )]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ConfirmEmailChange(ConfirmEmailChangeCommand command, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(command, ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record UpdateMyPhoneRequest(string? PhoneNumber);

    /// <summary>Guarda (o quita, si viene vacío) el teléfono de contacto del propio usuario, sin OTP. 204.</summary>
    [HttpPut("me/phone")]
    [Authorize]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.CustomerPortal,
        ActorType.PlatformAdmin
    )]
    [RateLimit("auth.g.credentials_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateMyPhone(UpdateMyPhoneRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new UpdateMyPhoneCommand(userId, request.PhoneNumber), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record RequestPhoneVerificationRequest(string PhoneNumber);

    [HttpPost("me/phone/change-request")]
    [Authorize]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.CustomerPortal,
        ActorType.PlatformAdmin
    )]
    [RateLimit("auth.g.credentials_manage")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestPhoneVerification(
        RequestPhoneVerificationRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new RequestPhoneVerificationCommand(userId, request.PhoneNumber),
            ct
        );

        return result.IsSuccess ? Accepted() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record ConfirmPhoneRequest(string Code);

    [HttpPost("me/phone/confirm")]
    [Authorize]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.CustomerPortal,
        ActorType.PlatformAdmin
    )]
    [RateLimit("auth.g.credentials_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ConfirmPhoneVerification(ConfirmPhoneRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new ConfirmPhoneVerificationCommand(userId, request.Code), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
