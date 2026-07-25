using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Invitations.Commands;
using TaxVision.Auth.Application.Invitations.Queries;
using TaxVision.Auth.Application.Users;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

[ApiController]
[Route("auth/invitations")]
public sealed class InvitationsController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    [HasPermission(PermissionCatalog.UsersInvite)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType<CreateInvitationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateInvitationRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CreateInvitationResponse>>(
            new CreateInvitationCommand(
                userId,
                request.TenantId,
                request.Email,
                request.ActorType,
                request.CustomerId,
                request.RoleIds
            ),
            ct
        );

        return result.IsSuccess
            ? Created($"/auth/invitations/{result.Value.InvitationId}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(PermissionCatalog.UsersInvite)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType<PagedResult<InvitationResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInvitations(
        [FromQuery] InvitationStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<PagedResult<InvitationResponse>>>(
            new GetInvitationsQuery(tenantId, status, page, size),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("accept")]
    [AllowAnonymous]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Accept(AcceptInvitationCommand command, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<UserResponse>>(command, ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{invitationId:guid}/resend")]
    [HasPermission(PermissionCatalog.UsersInvite)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Resend(Guid invitationId, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new ResendInvitationCommand(invitationId, userId, tenantId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{invitationId:guid}/cancel")]
    [HasPermission(PermissionCatalog.UsersInvite)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Cancel(Guid invitationId, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new CancelInvitationCommand(invitationId, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}

public sealed record CreateInvitationRequest(
    Guid TenantId,
    string Email,
    UserActorType ActorType,
    Guid? CustomerId,
    IReadOnlyList<Guid>? RoleIds = null
);
