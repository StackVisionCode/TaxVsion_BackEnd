using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.SeatAssignments.Commands.AssignSeatToUser;
using TaxVision.Subscription.Application.SeatAssignments.Commands.ReassignSeat;
using TaxVision.Subscription.Application.SeatAssignments.Commands.ReleaseSeatFromUser;
using TaxVision.Subscription.Application.Seats.Commands.PurchaseSeats;
using TaxVision.Subscription.Application.Seats.Commands.StartSeatCheckout;
using TaxVision.Subscription.Application.Seats.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

/// <summary>Seats del tenant (staff propio de la firma) — nunca un cliente final.</summary>
[ApiController]
[Route("seats")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class SeatsController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("subscription.f.seat_read")]
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
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<PagedResult<SeatResponse>>>(
            new GetTenantSeatsQuery(tenantId, status, type, userId, page, pageSize),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}")]
    [RateLimit("subscription.f.seat_read")]
    [ProducesResponseType<SeatResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SeatResponse>>(new GetSeatByIdQuery(tenantId, id), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Cotización server-authoritative (precio unitario + prorrateo a hoy) para comprar asientos.
    /// Read: la usa el modal de compra para mostrar "Comprar N asientos (+$X, prorrateado)" antes de cobrar.</summary>
    [HttpGet("quote")]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("subscription.f.seat_read")]
    [ProducesResponseType<SeatQuoteResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQuote(
        [FromQuery] string seatType,
        [FromQuery] int quantity,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SeatQuoteResponse>>(
            new GetSeatQuoteQuery(tenantId, seatType, quantity),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record PurchaseSeatsRequest(string SeatType, int Quantity, bool AutoRenew);

    [HttpPost("purchase")]
    [HasPermission(SubscriptionPermissions.SeatsManage)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("subscription.l.seat_purchase")]
    [ProducesResponseType<IReadOnlyList<Guid>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Purchase(PurchaseSeatsRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<Guid>>>(
            new PurchaseSeatsCommand(tenantId, request.SeatType, request.Quantity, request.AutoRenew, userId),
            ct
        );

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record StartSeatCheckoutRequest(
        string SeatType,
        int Quantity,
        bool AutoRenew,
        string PayerEmail,
        string SuccessUrl,
        string CancelUrl,
        string? Provider,
        string? Method
    );

    /// <summary>Compra de asientos por HOSTED-CHECKOUT (redirect): para el tenant SIN método en archivo.
    /// Devuelve la URL de checkout del provider; los asientos se aprovisionan al confirmarse el pago (webhook).
    /// El tenant CON método usa <c>POST seats/purchase</c> (cobro off-session, sin redirect).</summary>
    [HttpPost("checkout")]
    [HasPermission(SubscriptionPermissions.SeatsManage)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("subscription.l.seat_purchase")]
    [ProducesResponseType<StartSeatCheckoutResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> StartCheckout(StartSeatCheckoutRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<StartSeatCheckoutResponse>>(
            new StartSeatCheckoutCommand(
                tenantId,
                request.SeatType,
                request.Quantity,
                request.AutoRenew,
                request.PayerEmail,
                request.SuccessUrl,
                request.CancelUrl,
                string.IsNullOrWhiteSpace(request.Provider) ? "Stripe" : request.Provider,
                string.IsNullOrWhiteSpace(request.Method) ? "Card" : request.Method,
                userId
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Estado de una intención de checkout de asientos — lo consulta el front al volver del redirect
    /// hasta que el webhook la deja en <c>Provisioned</c> (o <c>Failed</c>).</summary>
    [HttpGet("checkout/{intentId:guid}")]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("subscription.f.seat_read")]
    [ProducesResponseType<SeatCheckoutStatusResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCheckoutStatus(Guid intentId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SeatCheckoutStatusResponse>>(
            new GetSeatCheckoutStatusQuery(tenantId, intentId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record AssignSeatRequest(Guid UserId);

    [HttpPost("{id:guid}/assign")]
    [HasPermission(SubscriptionPermissions.SeatsManage)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("subscription.g.seat_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Assign(Guid id, AssignSeatRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new AssignSeatToUserCommand(tenantId, id, request.UserId, userId),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record ReleaseSeatRequest(string? Reason);

    [HttpPost("{id:guid}/release")]
    [HasPermission(SubscriptionPermissions.SeatsManage)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("subscription.g.seat_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Release(Guid id, ReleaseSeatRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new ReleaseSeatFromUserCommand(tenantId, id, request.Reason, userId),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record ReassignSeatRequest(Guid ToUserId, string? Reason);

    [HttpPost("{id:guid}/reassign")]
    [HasPermission(SubscriptionPermissions.SeatsManage)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("subscription.g.seat_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reassign(Guid id, ReassignSeatRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new ReassignSeatCommand(tenantId, id, request.ToUserId, request.Reason, userId),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
