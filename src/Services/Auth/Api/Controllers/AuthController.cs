using BuildingBlocks.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Users.Commands;
using Wolverine;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;

namespace TaxVision.Auth.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController(IMessageBus bus) : ControllerBase
{
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
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
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(
        RefreshAccessTokenCommand command,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<LoginResponse>>(command, ct);

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
}
