using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Sessions.Commands;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Application.Users.Queries;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController(IMessageBus bus) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        LoginCommand command,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<LoginResponse>>(command, ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AuthTokensResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(
        RefreshAccessTokenCommand command,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<AuthTokensResponse>>(command, ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("revoke")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(
        RevokeRefreshTokenCommand command,
        CancellationToken ct)
    {
        await bus.InvokeAsync<Result>(command, ct);
        return NoContent();
    }

    /// <summary>Cierra la sesión actual (revoca la familia de refresh tokens y denylista el sid).</summary>
    [HttpPost("logout")]
    [Authorize]
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
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<MeResponse>>(new GetMeQuery(userId), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>JWKS público para validadores RS256. Con HS256 devuelve un set vacío.</summary>
    [HttpGet(".well-known/jwks.json")]
    [AllowAnonymous]
    [ResponseCache(Duration = 300)]
    public IActionResult Jwks([FromServices] IJwksProvider jwks)
        => Content(jwks.GetJwksJson(), "application/json");
}
