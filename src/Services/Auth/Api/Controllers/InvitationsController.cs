using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Invitations.Commands;
using TaxVision.Auth.Application.Users;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

[ApiController]
[Route("auth/invitations")]
public sealed class InvitationsController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "TenantAdmin,PlatformAdmin")]
    [ProducesResponseType<CreateInvitationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        CreateInvitationRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CreateInvitationResponse>>(
            new CreateInvitationCommand(
                userId,
                request.TenantId,
                request.Email,
                request.ActorType,
                request.CustomerId),
            ct);

        return result.IsSuccess
            ? Created($"/auth/invitations/{result.Value.InvitationId}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("accept")]
    [AllowAnonymous]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Accept(
        AcceptInvitationCommand command,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<UserResponse>>(command, ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{invitationId:guid}/cancel")]
    [Authorize(Roles = "TenantAdmin,PlatformAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Cancel(
        Guid invitationId,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new CancelInvitationCommand(invitationId, userId),
            ct);

        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var raw = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ??
                  User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out userId);
    }
}

public sealed record CreateInvitationRequest(
    Guid TenantId,
    string Email,
    UserActorType ActorType,
    Guid? CustomerId);
