using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.ServiceTokens.Commands;
using TaxVision.Auth.Application.Sessions.Commands;
using TaxVision.Auth.Application.TenantDomains;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Application.Users.Queries;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// TenantId es opcional a propósito: solo se usa en Development sin subdominios
    /// reales configurados (ver EffectiveLoginTenantResolver). En cualquier entorno con
    /// EnforceHostResolution=true se ignora siempre — el TenantId autoritativo sale del
    /// Host ya resuelto por TenantHostResolutionMiddleware, nunca del cliente.
    /// </summary>
    public sealed record LoginRequest(
        string Email,
        string Password,
        string? DeviceName = null,
        string? DeviceToken = null,
        Guid? TenantId = null
    );

    [HttpPost("login")]
    [AllowAnonymous]
    [RateLimitExempt(
        "Anónimo — protegido por ILoginThrottler.GetIpRetryAfterAsync/RegisterFailureAsync, un mecanismo de dominio (Redis) completamente separado del RateLimit HTTP de esta fase; sin tenant_id/sub en el JWT pre-login, TieredRateLimitEvaluator fallaría-abierto siempre."
    )]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        LoginRequest request,
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

        var command = new LoginCommand(
            tenantResult.Value,
            request.Email,
            request.Password,
            request.DeviceName,
            request.DeviceToken
        );
        var result = await bus.InvokeAsync<Result<LoginResponse>>(command, ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Fase 18 — ResolvedTenantId no se bindea del body: sale de IResolvedTenantContext
    /// (Host de la request, poblado por TenantHostResolutionMiddleware), igual que en Login.</summary>
    public sealed record RefreshRequest(string RefreshToken);

    [HttpPost("refresh")]
    [AllowAnonymous]
    [RateLimitExempt(
        "Anónimo — el refresh token en sí ya es el secreto portador (unguessable, host-binding en Fase 18.3); sin JWT propio que particionar, agregar protección HTTP nueva queda fuera de alcance de esta migración."
    )]
    [ProducesResponseType<AuthTokensResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(
        RefreshRequest request,
        [FromServices] IResolvedTenantContext tenantContext,
        CancellationToken ct
    )
    {
        var command = new RefreshAccessTokenCommand(request.RefreshToken, tenantContext.ResolvedTenantId);
        var result = await bus.InvokeAsync<Result<AuthTokensResponse>>(command, ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Grant client-credentials (M2M): emite un token de servicio para un tenant. Lo usan los workers
    /// de otros servicios (p. ej. Notification → CloudStorage) sin contexto de usuario.
    /// </summary>
    [HttpPost("service-token")]
    [AllowAnonymous]
    [RateLimitExempt(
        "Grant M2M — anónimo por diseño (todavía no existe un JWT en el momento de la llamada, es la propia emisión); usado por los HttpClients tipados de los otros 21 servicios, nunca por un usuario final."
    )]
    [ProducesResponseType<ServiceTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ServiceToken(IssueServiceTokenCommand command, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<ServiceTokenResponse>>(command, ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("revoke")]
    [Authorize]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.CustomerPortal,
        ActorType.PlatformAdmin
    )]
    [RateLimit("auth.g.session_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(RevokeRefreshTokenCommand command, CancellationToken ct)
    {
        await bus.InvokeAsync<Result>(command, ct);
        return NoContent();
    }

    /// <summary>Cierra la sesión actual (revoca la familia de refresh tokens y denylista el sid).</summary>
    [HttpPost("logout")]
    [Authorize]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.CustomerPortal,
        ActorType.PlatformAdmin
    )]
    [RateLimit("auth.g.session_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetSessionId(out var sessionId))
            return NoContent();

        await bus.InvokeAsync<Result>(new LogoutCommand(userId, sessionId), ct);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.CustomerPortal,
        ActorType.PlatformAdmin
    )]
    [RateLimit("auth.f.me_read")]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<MeResponse>>(new GetMeQuery(userId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>JWKS público para validadores RS256. Con HS256 devuelve un set vacío.</summary>
    [HttpGet(".well-known/jwks.json")]
    [AllowAnonymous]
    [ResponseCache(Duration = 300)]
    [RateLimitExempt(
        "Catálogo público cacheable (claves RSA de verificación, sin PII ni estado mutable) — mismo criterio que JwksController.Jwks de Signature en Fase 4.7."
    )]
    public IActionResult Jwks([FromServices] IJwksProvider jwks) => Content(jwks.GetJwksJson(), "application/json");
}
