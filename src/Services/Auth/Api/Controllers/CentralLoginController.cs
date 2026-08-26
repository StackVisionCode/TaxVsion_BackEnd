using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.CentralLogin.Commands;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// Login central multi-tenant (app.taxproffice.com). A diferencia de <see cref="AuthController"/>, es
/// cross-tenant a propósito: no resuelve un tenant del Host — autentica el email contra TODAS sus
/// oficinas. Flujo: discover-login (password) → [selector/MFA → handoff] → el frontend redirige al
/// subdominio y canjea el vale con from-ticket.
/// </summary>
[ApiController]
[Route("auth")]
public sealed class CentralLoginController(IMessageBus bus) : ControllerBase
{
    public sealed record DiscoverLoginRequest(string Email, string Password, string? DeviceName = null);

    /// <summary>Paso 1: password contra cada oficina. Devuelve vale directo (1 oficina, sin MFA) o selector.</summary>
    [HttpPost("discover-login")]
    [AllowAnonymous]
    [RateLimitExempt(
        "Anónimo y cross-tenant — sin JWT ni tenant que particionar, TieredRateLimitEvaluator fallaría-abierto; el freno real es ILoginThrottler por IP (Redis), igual que AuthController.Login."
    )]
    [ProducesResponseType<DiscoverLoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DiscoverLogin(DiscoverLoginRequest request, CancellationToken ct)
    {
        var command = new DiscoverLoginCommand(request.Email, request.Password, request.DeviceName);
        var result = await bus.InvokeAsync<Result<DiscoverLoginResponse>>(command, ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record HandoffRequest(Guid DiscoverySessionRef, Guid ChosenTenantId, string? MfaCode = null);

    /// <summary>Paso 2 (solo con selector/MFA): elige oficina, resuelve MFA y emite el vale.</summary>
    [HttpPost("session/handoff")]
    [AllowAnonymous]
    [RateLimitExempt(
        "Anónimo — el password ya se validó en discover-login; acá solo se elige entre oficinas ya autenticadas (ref de sesión unguessable, TTL corto) y se resuelve MFA. Sin JWT que particionar."
    )]
    [ProducesResponseType<HandoffTicketView>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Handoff(HandoffRequest request, CancellationToken ct)
    {
        var command = new IssueHandoffTicketCommand(
            request.DiscoverySessionRef,
            request.ChosenTenantId,
            request.MfaCode
        );
        var result = await bus.InvokeAsync<Result<HandoffTicketView>>(command, ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record FromTicketRequest(Guid Ticket, string? DeviceName = null);

    /// <summary>Paso 3 (en el subdominio destino): canjea el vale de un solo uso por tokens de sesión.</summary>
    [HttpPost("session/from-ticket")]
    [AllowAnonymous]
    [RateLimitExempt(
        "Anónimo — el vale de handoff es el secreto portador (unguessable, un solo uso vía GETDEL, TTL 60s); sin JWT propio que particionar, mismo criterio que AuthController.Refresh."
    )]
    [ProducesResponseType<HandoffSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> FromTicket(FromTicketRequest request, CancellationToken ct)
    {
        var command = new ExchangeHandoffTicketCommand(request.Ticket, request.DeviceName);
        var result = await bus.InvokeAsync<Result<HandoffSessionResponse>>(command, ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
