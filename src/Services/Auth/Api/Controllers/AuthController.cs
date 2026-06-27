using BuildingBlocks.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Users.Commands;
using Wolverine;
using BuildingBlocks.Web.Results;

namespace TaxVision.Auth.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController(IMessageBus bus) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        RegisterCommand command,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<UserResponse>>(command, ct);

        return result.IsSuccess
            ? Created($"/auth/users/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

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

    [HttpPost("activate-admin")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ActivateAdmin(
        ActivateTenantAdminCommand command,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<UserResponse>>(command, ct);

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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(
        RevokeRefreshTokenCommand command,
        CancellationToken ct)
    {
        await bus.InvokeAsync<Result>(command, ct);
        return NoContent();
    }
}
