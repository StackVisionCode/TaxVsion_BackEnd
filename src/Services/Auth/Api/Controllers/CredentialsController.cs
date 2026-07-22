using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Credentials.Commands;
using TaxVision.Auth.Application.TenantDomains;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class CredentialsController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// TenantId opcional — mismo criterio que AuthController.Login (ver
    /// EffectiveLoginTenantResolver): con EnforceHostResolution=true se ignora
    /// siempre y se usa el Host resuelto; nunca lo dicta el cliente.
    /// </summary>
    public sealed record ForgotPasswordRequest(string Email, Guid? TenantId = null);

    /// <summary>Solicita recuperación de contraseña. Siempre responde 202 (anti-enumeración).</summary>
    [HttpPost("password/forgot")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequest request,
        [FromServices] IResolvedTenantContext tenantContext,
        [FromServices] IOptions<TenantDomainOptions> tenantDomainOptions,
        CancellationToken ct
    )
    {
        var tenantResult = EffectiveLoginTenantResolver.Resolve(
            tenantDomainOptions.Value.EnforceHostResolution,
            tenantContext.ResolvedTenantId,
            request.TenantId
        );
        if (tenantResult.IsFailure)
            return StatusCode(tenantResult.Error.ToHttpStatusCode(), tenantResult.Error);

        await bus.InvokeAsync<Result>(new ForgotPasswordCommand(tenantResult.Value, request.Email), ct);
        return Accepted();
    }

    [HttpPost("password/reset")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ResetPassword(ResetPasswordCommand command, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(command, ct);

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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ConfirmPhoneVerification(ConfirmPhoneRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new ConfirmPhoneVerificationCommand(userId, request.Code), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
