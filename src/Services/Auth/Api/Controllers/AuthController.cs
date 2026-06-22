using BuildingBlocks.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Users.Commands;
using Wolverine;

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
            : BadRequest(result.Error);
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
            : BadRequest(result.Error);
    }
}
