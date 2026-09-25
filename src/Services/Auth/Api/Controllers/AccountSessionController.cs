using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.AccountSessions.Commands;
using TaxVision.Auth.Application.CentralLogin.Commands;
using TaxVision.Auth.Application.Sessions.Commands;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Infrastructure.Security;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// Sesión del Account del Landing. El access token viaja en el body y vive en memoria; el refresh va solo en
/// la cookie <see cref="AccountSessionCookie"/> y nunca en el body. Entradas: desde el CRM ("Manage
/// subscription", mismo <c>sid</c>) o directa por el login central (sesión única con takeover).
/// </summary>
[ApiController]
[Route("auth/account")]
public sealed class AccountSessionController(IMessageBus bus, IOptions<RefreshTokenOptions> refreshOptions)
    : ControllerBase
{
    private const string AnonymousSessionReason =
        "Anónimo — el secreto portador es el vale (un solo uso, TTL corto) o la cookie HttpOnly del Account; sin JWT que particionar. Lo acota el limiter nativo \"auth-account-session\" por IP y el chequeo de Origin.";

    public sealed record TicketRequest(Guid Ticket, string? DeviceName = null);

    /// <summary>Tokens del Account. Si hace falta confirmar el takeover, viene el vale en vez del token.</summary>
    public sealed record AccountSessionResponse(
        string? AccessToken,
        int ExpiresInSeconds,
        bool TakeoverRequired = false,
        string? TakeoverTicket = null,
        int? TakeoverTicketExpiresInSeconds = null
    );

    /// <summary>CRM → vale de un solo uso para abrir el Account con la misma sesión.</summary>
    [HttpPost("handoff")]
    [Authorize]
    [AllowActorTypes(ActorType.TenantAdmin)]
    [HasPermission(PermissionCatalog.BillingView)]
    [RateLimit("auth.g.account_handoff")]
    [ProducesResponseType<AccountHandoffView>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Handoff(CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetSessionId(out var sessionId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<AccountHandoffView>>(
            new IssueAccountHandoffCommand(userId, sessionId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Landing: canjea el vale del CRM y suma la cadena del Account a esa sesión.</summary>
    [HttpPost("session/from-handoff")]
    [AllowAnonymous]
    [RequireAccountOrigin]
    [RateLimitExempt(AnonymousSessionReason)]
    [EnableRateLimiting("auth-account-session")]
    [ProducesResponseType<AccountSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> FromHandoff(TicketRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<AuthTokensResponse>>(
            new ExchangeAccountHandoffCommand(request.Ticket),
            ct
        );
        return result.IsSuccess
            ? SessionStarted(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Landing: entrada directa, canjea el vale del login central (puede pedir takeover).</summary>
    [HttpPost("session/from-login")]
    [AllowAnonymous]
    [RequireAccountOrigin]
    [RateLimitExempt(AnonymousSessionReason)]
    [EnableRateLimiting("auth-account-session")]
    [ProducesResponseType<AccountSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> FromLogin(TicketRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<HandoffSessionResponse>>(
            new ExchangeHandoffTicketCommand(request.Ticket, request.DeviceName, SessionSurface.Account),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        var outcome = result.Value;
        if (outcome.TakeoverRequired)
        {
            return Ok(
                new AccountSessionResponse(
                    null,
                    0,
                    TakeoverRequired: true,
                    TakeoverTicket: outcome.TakeoverTicket,
                    TakeoverTicketExpiresInSeconds: outcome.TakeoverTicketExpiresInSeconds
                )
            );
        }

        return SessionStarted(
            new AuthTokensResponse(outcome.AccessToken!, outcome.RefreshToken!, outcome.ExpiresInSeconds)
        );
    }

    /// <summary>Landing: el usuario confirmó cerrar su otra sesión.</summary>
    [HttpPost("session/takeover")]
    [AllowAnonymous]
    [RequireAccountOrigin]
    [RateLimitExempt(AnonymousSessionReason)]
    [EnableRateLimiting("auth-account-session")]
    [ProducesResponseType<AccountSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Takeover(TicketRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<LoginResponse>>(
            new TakeoverSessionCommand(request.Ticket, request.DeviceName, SessionSurface.Account),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        // El takeover siempre termina en tokens: la política del Account ya descartó el enrolamiento de MFA.
        return SessionStarted(result.Value.Tokens!);
    }

    /// <summary>Rota el refresh de la cookie. Un 401 borra la cookie: la sesión del Account terminó.</summary>
    [HttpPost("session/refresh")]
    [AllowAnonymous]
    [RequireAccountOrigin]
    [RateLimitExempt(AnonymousSessionReason)]
    [EnableRateLimiting("auth-account-session")]
    [ProducesResponseType<AccountSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var refreshToken = AccountSessionCookie.Read(Request);
        if (refreshToken is null)
            return StatusCode(StatusCodes.Status401Unauthorized, InvalidSession);

        // Sin binding de Host: el Account llama por api.*, y el tenant sale del propio refresh token.
        var result = await bus.InvokeAsync<Result<AuthTokensResponse>>(
            new RefreshAccessTokenCommand(refreshToken, ResolvedTenantId: null, SessionSurface.Account),
            ct
        );
        if (result.IsSuccess)
            return SessionStarted(result.Value);

        if (result.Error.ToHttpStatusCode() == StatusCodes.Status401Unauthorized)
            AccountSessionCookie.Delete(Response);
        return StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Cierra el Account. Si se entró desde el CRM, el CRM sigue abierto.</summary>
    [HttpPost("session/logout")]
    [AllowAnonymous]
    [RequireAccountOrigin]
    [RateLimitExempt(AnonymousSessionReason)]
    [EnableRateLimiting("auth-account-session")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await bus.InvokeAsync<Result>(new EndAccountSessionCommand(AccountSessionCookie.Read(Request)), ct);
        AccountSessionCookie.Delete(Response);
        return NoContent();
    }

    private static readonly Error InvalidSession = new(
        "Auth.InvalidRefreshToken",
        "Refresh token is invalid or expired."
    );

    private OkObjectResult SessionStarted(AuthTokensResponse tokens)
    {
        AccountSessionCookie.Append(
            Response,
            tokens.RefreshToken,
            DateTime.UtcNow.AddDays(refreshOptions.Value.ExpirationDays)
        );
        return Ok(new AccountSessionResponse(tokens.AccessToken, tokens.ExpiresInSeconds));
    }
}
