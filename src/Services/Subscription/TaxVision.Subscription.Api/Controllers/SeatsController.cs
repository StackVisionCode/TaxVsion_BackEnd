using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.SeatAssignments.Commands.AssignSeatToUser;
using TaxVision.Subscription.Application.SeatAssignments.Commands.ReassignSeat;
using TaxVision.Subscription.Application.SeatAssignments.Commands.ReleaseSeatFromUser;
using TaxVision.Subscription.Application.Seats.Commands.PurchaseSeats;
using TaxVision.Subscription.Application.Seats.Commands.RenewSeat;
using TaxVision.Subscription.Application.Seats.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

[ApiController]
[Route("seats")]
[Authorize]
public sealed class SeatsController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<SeatResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSeats(
        [FromQuery] string? status,
        [FromQuery] string? type,
        [FromQuery] Guid? userId,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken ct
    )
    {
        if (!TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<PagedResult<SeatResponse>>>(
            new GetTenantSeatsQuery(tenantId, status, type, userId, page, pageSize),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<SeatResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SeatResponse>>(new GetSeatByIdQuery(tenantId, id), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record PurchaseSeatsRequest(string SeatType, int Quantity, bool AutoRenew);

    [HttpPost("purchase")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType<IReadOnlyList<Guid>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Purchase(PurchaseSeatsRequest request, CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<Guid>>>(
            new PurchaseSeatsCommand(tenantId, request.SeatType, request.Quantity, request.AutoRenew, userId),
            ct
        );

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record AssignSeatRequest(Guid UserId);

    [HttpPost("{id:guid}/assign")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Assign(Guid id, AssignSeatRequest request, CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new AssignSeatToUserCommand(tenantId, id, request.UserId, userId),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record ReleaseSeatRequest(string? Reason);

    [HttpPost("{id:guid}/release")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Release(Guid id, ReleaseSeatRequest request, CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new ReleaseSeatFromUserCommand(tenantId, id, request.Reason, userId),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record ReassignSeatRequest(Guid ToUserId, string? Reason);

    [HttpPost("{id:guid}/reassign")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reassign(Guid id, ReassignSeatRequest request, CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new ReassignSeatCommand(tenantId, id, request.ToUserId, request.Reason, userId),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Renovación manual (mientras no exista Billing).</summary>
    [HttpPost("{id:guid}/renew")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Renew(Guid id, CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new RenewSeatCommand(tenantId, id, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private bool TryGetTenantAndUser(out Guid tenantId, out Guid userId)
    {
        userId = Guid.Empty;
        if (!Guid.TryParse(User.FindFirst("tenant_id")?.Value, out tenantId))
            return false;

        var raw =
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out userId);
    }
}
