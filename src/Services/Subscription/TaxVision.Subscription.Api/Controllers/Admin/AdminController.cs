using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Admin.Queries;
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
        var result = await bus.InvokeAsync<Result<IReadOnlyList<UpcomingRenewalResponse>>>(new GetUpcomingRenewalsQuery(daysAhead), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("expired-seats")]
    [ProducesResponseType<PagedResult<AdminSeatResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExpiredSeats([FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<PagedResult<AdminSeatResponse>>>(new GetExpiredSeatsQuery(page, pageSize), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("past-due-subscriptions")]
    [ProducesResponseType<PagedResult<AdminSubscriptionResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPastDueSubscriptions([FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<PagedResult<AdminSubscriptionResponse>>>(new GetPastDueSubscriptionsQuery(page, pageSize), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
