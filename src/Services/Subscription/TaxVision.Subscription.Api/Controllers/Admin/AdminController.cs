using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Admin.Queries;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers.Admin;

/// <summary>Consultas administrativas cross-tenant. Solo PlatformAdmin — un tenant admin
/// no puede ver datos de otros tenants.</summary>
[ApiController]
[Route("admin/subscription")]
[Authorize(Roles = "PlatformAdmin")]
public sealed class AdminController(IMessageBus bus) : ControllerBase
{
    [HttpGet("upcoming-renewals")]
    [ProducesResponseType<IReadOnlyList<UpcomingRenewalResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUpcomingRenewals([FromQuery] int daysAhead, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<UpcomingRenewalResponse>>>(
            new GetUpcomingRenewalsQuery(daysAhead),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("expired-seats")]
    [ProducesResponseType<PagedResult<AdminSeatResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExpiredSeats(
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<PagedResult<AdminSeatResponse>>>(
            new GetExpiredSeatsQuery(page, pageSize),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("past-due-subscriptions")]
    [ProducesResponseType<PagedResult<AdminSubscriptionResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPastDueSubscriptions(
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<PagedResult<AdminSubscriptionResponse>>>(
            new GetPastDueSubscriptionsQuery(page, pageSize),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Fuerza el recalculo del TenantEntitlementSnapshot de un tenant y republica
    /// TenantEntitlementsChangedIntegrationEvent — via de escape operativa para reconciliar un
    /// tenant que quedo con una suscripcion valida pero sin snapshot (p.ej. por una falla
    /// transitoria en un intento anterior). Idempotente: recalcula desde el estado actual,
    /// no cambia nada del lado de la suscripcion/seats/add-ons.
    /// </summary>
    [HttpPost("tenants/{tenantId:guid}/recalculate-entitlements")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RecalculateEntitlements(Guid tenantId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(new RecalculateEntitlementsCommand(tenantId), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
