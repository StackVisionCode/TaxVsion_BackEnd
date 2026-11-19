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
using TaxVision.Mobile.Application.Trips;
using TaxVision.Mobile.Application.Trips.Commands;
using TaxVision.Mobile.Application.Trips.Queries;
using Wolverine;

namespace TaxVision.Mobile.Api.Controllers;

[ApiController]
[Authorize]
[Route("mobile/mileage-configuration")]
public sealed class MileageConfigurationController(IMessageBus bus, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    [AllowActorTypes(ActorType.CustomerPortal, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [HasPermission(MobilePermissions.ConfigurationRead)]
    [RateLimit("mobile.f.trip_read")]
    [ProducesResponseType<MileageConfigurationResponse>(200)]
    public async Task<IActionResult> Get(
        CancellationToken ct,
        [FromQuery] string purpose = "business",
        [FromQuery] DateOnly? date = null
    )
    {
        if (!Identity(out var t))
            return Unauthorized();
        return Respond(
            await bus.InvokeAsync<Result<MileageConfigurationResponse>>(
                new MileageConfigurationQuery(t, purpose, date ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)),
                ct
            ),
            Ok
        );
    }

    [HttpGet("rates")]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [HasPermission(MobilePermissions.RatesManage)]
    [RateLimit("mobile.f.trip_read")]
    [ProducesResponseType<PagedResult<MileageRateResponse>>(200)]
    public async Task<IActionResult> Rates(CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int size = 50)
    {
        if (!Identity(out var t))
            return Unauthorized();
        return Respond(
            await bus.InvokeAsync<Result<PagedResult<MileageRateResponse>>>(
                new ListMileageRatesQuery(t, page, size),
                ct
            ),
            Ok
        );
    }

    [HttpPost("rates")]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [HasPermission(MobilePermissions.RatesManage)]
    [RateLimit("mobile.g.trip_write")]
    [ProducesResponseType<MileageRateResponse>(201)]
    public async Task<IActionResult> Create(CreateRateInput request, CancellationToken ct)
    {
        if (!Identity(out var t))
            return Unauthorized();
        return Respond(
            await bus.InvokeAsync<Result<MileageRateResponse>>(new CreateMileageRateCommand(t, request), ct),
            v => StatusCode(201, v)
        );
    }

    private bool Identity(out Guid tenant)
    {
        Response.Headers.CacheControl = "no-store";
        return this.TryGetTenantAndUser(out tenant, out _);
    }

    private IActionResult Respond<T>(Result<T> result, Func<T, IActionResult> success) =>
        result.IsSuccess ? success(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
}
