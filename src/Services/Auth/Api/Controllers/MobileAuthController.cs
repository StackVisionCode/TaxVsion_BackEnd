using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Auth.Application.CentralLogin.Commands;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// Autenticación del cliente de oficina desde aplicaciones móviles sobre el host compartido de la API.
/// El primer envío valida email y contraseña y devuelve las oficinas permitidas. El segundo usa la
/// referencia efímera y el tenant elegido; la contraseña no vuelve a viajar ni se confía en un tenant
/// que no estuviera dentro del conjunto autenticado.
/// </summary>
[ApiController]
[Route("auth/mobile")]
public sealed class MobileAuthController(IMessageBus bus) : ControllerBase
{
    public sealed record MobileLoginRequest(
        string? Email = null,
        string? Password = null,
        Guid? TenantId = null,
        Guid? DiscoverySessionRef = null,
        string? MfaCode = null,
        bool RememberDevice = false,
        string? DeviceName = null,
        string? DeviceToken = null
    );

    public sealed record MobileOfficeView(Guid TenantId, string Name, string Subdomain, bool MfaRequired);

    public sealed record MobileLoginResponse(
        bool RequiresTenantSelection,
        Guid? DiscoverySessionRef,
        IReadOnlyList<MobileOfficeView> Offices,
        string? AccessToken,
        string? RefreshToken,
        int? ExpiresInSeconds,
        bool MfaSetupRequired,
        bool TakeoverRequired,
        string? TakeoverTicket,
        int? TakeoverTicketExpiresInSeconds,
        string? DeviceToken
    )
    {
        public static MobileLoginResponse ForSelection(Guid sessionRef, IReadOnlyList<DiscoverOfficeView> offices) =>
            new(
                true,
                sessionRef,
                offices
                    .Select(office => new MobileOfficeView(
                        office.TenantId,
                        office.TenantName,
                        office.Subdomain,
                        office.MfaRequired
                    ))
                    .ToList(),
                null,
                null,
                null,
                false,
                false,
                null,
                null,
                null
            );

        public static MobileLoginResponse ForSession(HandoffSessionResponse session) =>
            new(
                false,
                null,
                [],
                session.AccessToken,
                session.RefreshToken,
                session.ExpiresInSeconds,
                session.MfaSetupRequired,
                session.TakeoverRequired,
                session.TakeoverTicket,
                session.TakeoverTicketExpiresInSeconds,
                session.DeviceToken
            );
    }

    /// <summary>
    /// Fase 1: email + password, sin tenant, devuelve una referencia y las oficinas del cliente.
    /// Fase 2: discoverySessionRef + tenantId materializa la sesión. Si la app ya conoce el tenant,
    /// puede enviarlo en la fase 1 y completar ambas fases en una sola petición.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [RateLimitExempt(
        "Anónimo y cross-tenant; ILoginThrottler limita por IP y la referencia efímera limita la selección a oficinas ya autenticadas."
    )]
    [ProducesResponseType<MobileLoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(MobileLoginRequest request, CancellationToken ct)
    {
        var sessionRef = request.DiscoverySessionRef;

        if (sessionRef is null)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new Error("Auth.MobileCredentialsRequired", "Email and password are required."));

            var discovery = await bus.InvokeAsync<Result<DiscoverLoginResponse>>(
                new DiscoverLoginCommand(
                    request.Email,
                    request.Password,
                    request.DeviceName,
                    UserAccountKind.Portal,
                    request.DeviceToken,
                    SessionSurface.Workspace,
                    ForceSelection: true
                ),
                ct
            );
            if (discovery.IsFailure)
                return StatusCode(discovery.Error.ToHttpStatusCode(), discovery.Error);

            sessionRef = discovery.Value.DiscoverySessionRef;
            var offices = discovery.Value.Offices;
            if (sessionRef is null || offices is null)
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new Error("Auth.MobileDiscoveryInvalid", "The sign-in session could not be created.")
                );

            if (request.TenantId is null)
                return Ok(MobileLoginResponse.ForSelection(sessionRef.Value, offices));
        }

        if (request.TenantId is null)
            return BadRequest(new Error("Auth.MobileTenantRequired", "TenantId is required to complete sign-in."));

        var handoff = await bus.InvokeAsync<Result<HandoffTicketView>>(
            new IssueHandoffTicketCommand(
                sessionRef.Value,
                request.TenantId.Value,
                request.MfaCode,
                UserAccountKind.Portal,
                request.RememberDevice
            ),
            ct
        );
        if (handoff.IsFailure)
            return StatusCode(handoff.Error.ToHttpStatusCode(), handoff.Error);

        var exchange = await bus.InvokeAsync<Result<HandoffSessionResponse>>(
            new ExchangeHandoffTicketCommand(handoff.Value.Ticket, request.DeviceName, SessionSurface.Workspace),
            ct
        );

        return exchange.IsSuccess
            ? Ok(MobileLoginResponse.ForSession(exchange.Value))
            : StatusCode(exchange.Error.ToHttpStatusCode(), exchange.Error);
    }

    public sealed record MobileRefreshRequest(string RefreshToken);

    /// <summary>
    /// Renueva una sesión móvil desde api.taxoffix.io. El refresh token opaco ya está ligado en base
    /// de datos al usuario, tenant, sesión y superficie; no se compara contra el tenant Platform del
    /// host compartido porque ese host no representa una oficina.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [RateLimitExempt("El refresh token opaco es el secreto portador y el limitador auth-refresh acota por IP.")]
    [EnableRateLimiting("auth-refresh")]
    [ProducesResponseType<AuthTokensResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(MobileRefreshRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<AuthTokensResponse>>(
            new RefreshAccessTokenCommand(
                request.RefreshToken,
                ResolvedTenantId: null,
                Surface: SessionSurface.Workspace
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
