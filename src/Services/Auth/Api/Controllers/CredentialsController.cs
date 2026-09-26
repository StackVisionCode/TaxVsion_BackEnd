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

    [HttpPost("password/reset")]
    [AllowAnonymous]
    [RateLimitExempt(
        "Anónimo (token de reset por email) — máx. 5 intentos por token es la protección real (Fase 18.2), no requiere tenant/user JWT."
    )]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ResetPassword(ResetPasswordCommand command, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(command, ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record ValidateResetTokenRequest(string Token);

    /// <summary>¿El enlace de reset todavía sirve? 204 si sí; 401 si caducó, ya se usó o fue anulado. No lo consume
    /// ni cuenta intentos: la página lo consulta al abrirse para avisar antes de pedir la contraseña.</summary>
    [HttpPost("password/reset/validate")]
    [AllowAnonymous]
    [RateLimitExempt(
        "Anónimo (token de reset por email) — el token es imposible de adivinar y el Gateway acota la ruta por IP (PreAuthByIp)."
    )]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ValidateResetToken(ValidateResetTokenRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(new ValidatePasswordResetTokenQuery(request.Token), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
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
