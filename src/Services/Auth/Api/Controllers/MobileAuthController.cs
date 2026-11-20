using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.CentralLogin.Commands;
using TaxVision.Auth.Application.Mfa.Commands;
using TaxVision.Auth.Application.Sessions.Commands;
using TaxVision.Auth.Application.TenantDomains;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// Autenticación de las apps móviles (cliente y soporte de plataforma) sobre el host compartido de la API. Sin
/// lógica: cada acción es un mensaje. Todo viaja con superficie <see cref="SessionSurface.Mobile"/>: un token web
/// no se renueva ni se toma por estas rutas, así que saltarse el binding de host aquí no abre la cadena web.
/// </summary>
[ApiController]
[Route("auth/mobile")]
public sealed class MobileAuthController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Primera vuelta: email + password, devuelve las oficinas del cliente y una referencia efímera. Segunda:
    /// referencia + oficina (+ código MFA) y devuelve la sesión. Con la oficina en la primera vuelta, son una.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [RateLimitExempt(
        "Anónimo y cross-tenant; el freno es ILoginThrottler por IP y por email (Redis), más PreAuthByIp en el Gateway."
    )]
    [ProducesResponseType<MobileSignInResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<Error>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(MobileSignInCommand request, CancellationToken ct) =>
        ToActionResult(await bus.InvokeAsync<Result<MobileSignInResponse>>(request, ct));

    public sealed record MobileRefreshRequest(string RefreshToken);

    /// <summary>
    /// Renueva una sesión móvil. El refresh token opaco ya está ligado en base a usuario, tenant, sesión y
    /// superficie; no hay host de oficina contra el que comparar, por eso solo acepta la cadena Mobile.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [RateLimitExempt("El refresh token opaco es el secreto portador y el limitador auth-refresh acota por IP.")]
    [EnableRateLimiting("auth-refresh")]
    [ProducesResponseType<AuthTokensResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(MobileRefreshRequest request, CancellationToken ct) =>
        ToActionResult(
            await bus.InvokeAsync<Result<AuthTokensResponse>>(
                new RefreshAccessTokenCommand(request.RefreshToken, ResolvedTenantId: null, SessionSurface.Mobile),
                ct
            )
        );

    public sealed record PlatformLoginRequest(
        string Email,
        string Password,
        string? DeviceName = null,
        string? DeviceToken = null,
        Guid? TenantId = null
    );

    /// <summary>
    /// App de soporte de plataforma: el login clásico (clave, bloqueo por cuenta y MFA) contra el tenant del host,
    /// con sesión del canal móvil para convivir con la consola web. La política solo la abre a quien tiene app.
    /// </summary>
    [HttpPost("platform/login")]
    [AllowAnonymous]
    [RateLimitExempt(
        "Anónimo; el freno es ILoginThrottler por IP y el bloqueo por cuenta del login clásico, más PreAuthByIp en el Gateway."
    )]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<Error>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> PlatformLogin(
        PlatformLoginRequest request,
        [FromServices] IResolvedTenantContext tenantContext,
        [FromServices] IOptions<TenantDomainOptions> tenantDomainOptions,
        CancellationToken ct
    )
    {
        var tenant = EffectiveLoginTenantResolver.Resolve(
            tenantDomainOptions.Value.EnforceHostResolution,
            tenantContext.ResolvedTenantId,
            request.TenantId
        );
        if (tenant.IsFailure)
            return StatusCode(tenant.Error.ToHttpStatusCode(), tenant.Error);

        return ToActionResult(
            await bus.InvokeAsync<Result<LoginResponse>>(
                new LoginCommand(
                    tenant.Value,
                    request.Email,
                    request.Password,
                    request.DeviceName,
                    request.DeviceToken,
                    UserAccountKind.Staff,
                    SessionSurface.Mobile
                ),
                ct
            )
        );
    }

    public sealed record PlatformMfaVerifyRequest(
        string LoginTicket,
        string? Code = null,
        string? RecoveryCode = null,
        bool RememberDevice = false,
        string? DeviceName = null
    );

    /// <summary>Paso 2 del login de la app de soporte: el código del desafío, con sesión del canal móvil.</summary>
    [HttpPost("platform/mfa/verify")]
    [AllowAnonymous]
    [RateLimitExempt(
        "Anónimo (identificado por LoginTicket); VerifyMfaChallengeHandler limita intentos por desafío y el Gateway aplica PreAuthByIp."
    )]
    [ProducesResponseType<MfaVerifyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PlatformVerifyMfa(PlatformMfaVerifyRequest request, CancellationToken ct) =>
        ToActionResult(
            await bus.InvokeAsync<Result<MfaVerifyResponse>>(
                new VerifyMfaChallengeCommand(
                    request.LoginTicket,
                    request.Code,
                    request.RecoveryCode,
                    request.RememberDevice,
                    request.DeviceName,
                    SessionSurface.Mobile
                ),
                ct
            )
        );

    public sealed record MobileTakeoverRequest(Guid Ticket, string? DeviceName = null);

    /// <summary>El cliente confirmó cerrar su otra sesión de la app. La sesión web no se toca.</summary>
    [HttpPost("session/takeover")]
    [AllowAnonymous]
    [RateLimitExempt(
        "Anónimo: el vale de takeover (Guid, un solo uso, TTL 2 min) es el secreto portador y lo emitió un login que ya pasó por ILoginThrottler."
    )]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Takeover(MobileTakeoverRequest request, CancellationToken ct) =>
        ToActionResult(
            await bus.InvokeAsync<Result<LoginResponse>>(
                new TakeoverSessionCommand(request.Ticket, request.DeviceName, SessionSurface.Mobile),
                ct
            )
        );

    private ObjectResult ToActionResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
}
