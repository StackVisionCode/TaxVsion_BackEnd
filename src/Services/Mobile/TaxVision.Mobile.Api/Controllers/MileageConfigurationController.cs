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
using TaxVision.Mobile.Domain.Trips;
using TaxVision.Mobile.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace TaxVision.Mobile.Api.Controllers;

[ApiController]
[Authorize]
[Route("mobile/mileage-configuration")]
public sealed class MileageConfigurationController(IMessageBus bus, TimeProvider clock, MobileDbContext db) : ControllerBase
{
    public sealed record RetentionPolicyRequest(int? RetentionDays);

    [HttpGet("retention")]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [HasPermission(MobilePermissions.RatesManage)]
    [RateLimit("mobile.f.trip_read")]
    public async Task<IActionResult> GetRetention(CancellationToken ct)
    {
        if (!Identity(out var tenant)) return Unauthorized();
        var policy = await db.MileageRetentionPolicies.SingleOrDefaultAsync(x => x.TenantId == tenant, ct);
        return Ok(new { retentionDays = policy?.RetentionDays, configured = policy?.RetentionDays is not null });
    }

    [HttpPut("retention")]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [HasPermission(MobilePermissions.RatesManage)]
    [RateLimit("mobile.g.trip_write")]
    public async Task<IActionResult> SetRetention(RetentionPolicyRequest request, CancellationToken ct)
    {
        if (!Identity(out var tenant)) return Unauthorized();
        if (request.RetentionDays is <= 0 or > 3650) return BadRequest(new { code = "Mobile.Retention.InvalidDays" });
        var policy = await db.MileageRetentionPolicies.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.TenantId == tenant, ct);
        if (policy is null) db.MileageRetentionPolicies.Add(MileageRetentionPolicy.Create(tenant, request.RetentionDays, clock.GetUtcNow().UtcDateTime));
        else policy.Update(request.RetentionDays, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return Ok(new { retentionDays = request.RetentionDays, configured = request.RetentionDays is not null });
    }
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
