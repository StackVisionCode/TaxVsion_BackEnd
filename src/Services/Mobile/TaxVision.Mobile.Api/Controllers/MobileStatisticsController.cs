using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Mobile.Application.Trips;
using TaxVision.Mobile.Application.Trips.Queries;
using Wolverine;

namespace TaxVision.Mobile.Api.Controllers;

[ApiController]
[Authorize]
[AllowActorTypes(ActorType.CustomerPortal)]
[Route("mobile/statistics")]
public sealed class MobileStatisticsController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [HasPermission(MobilePermissions.TripsRead)]
    [RateLimit("mobile.f.trip_read")]
    [ProducesResponseType<TripStatisticsResponse>(200)]
    public async Task<IActionResult> Get(CancellationToken ct, [FromQuery] string? timeZone = null)
    {
        if (!Identity(out var t, out var c))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result<TripStatisticsResponse>>(
            new TripStatisticsQuery(t, c, timeZone ?? User.FindFirst("zoneinfo")?.Value ?? "Etc/UTC"),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("daily")]
    [HasPermission(MobilePermissions.TripsRead)]
    [RateLimit("mobile.f.trip_read")]
    [ProducesResponseType<IReadOnlyList<DailyMileageResponse>>(200)]
    public async Task<IActionResult> Daily(
        CancellationToken ct,
        [FromQuery] int days = 7,
        [FromQuery] DateOnly? endDate = null,
        [FromQuery] string? timeZone = null
    )
    {
        if (!Identity(out var t, out var c))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result<IReadOnlyList<DailyMileageResponse>>>(
            new DailyMileageQuery(t, c, timeZone ?? User.FindFirst("zoneinfo")?.Value ?? "Etc/UTC", days, endDate),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private bool Identity(out Guid tenant, out Guid customer)
    {
        Response.Headers.CacheControl = "no-store";
        customer = Guid.Empty;
        return this.TryGetTenantAndUser(out tenant, out _)
            && User.TryGetCustomerId(out customer)
            && customer != Guid.Empty;
    }
}
