using System.Text;
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
using TaxVision.Mobile.Application.Staff;
using TaxVision.Mobile.Application.Trips;
using TaxVision.Mobile.Application.Vehicles;
using TaxVision.Mobile.Domain.Trips;
using Wolverine;

namespace TaxVision.Mobile.Api.Controllers;

/// <summary>
/// Lectura del kilometraje de un cliente para el personal (solo GET). La oficina y el usuario salen del token;
/// la visibilidad (cliente asignado o customers.view_all) la decide el handler, que responde 404 si no se ve.
/// Las rutas del portal (/mobile/trips) no cambian.
/// </summary>
[ApiController]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
[Route("mobile/staff/customers/{customerId:guid}")]
[Produces("application/json")]
public sealed class StaffMileageController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    [HttpGet("trips")]
    [HasPermission(MileagePermissions.View)]
    [RateLimit("mobile.f.staff_trip_read")]
    [ProducesResponseType<PagedResult<StaffTripResponse>>(200)]
    public async Task<IActionResult> Trips(
        Guid customerId,
        CancellationToken ct,
        [FromQuery] int? taxYear = null,
        [FromQuery] string? purpose = null,
        [FromQuery] TripStatus? status = null,
        [FromQuery] Guid? vehicleId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20
    )
    {
        if (await Scope(customerId, ct) is not { } scope)
            return Unauthorized();
        var query = new StaffListTripsQuery(scope, taxYear, purpose, status, vehicleId, page, pageSize);
        return Respond(await bus.InvokeAsync<Result<PagedResult<StaffTripResponse>>>(query, ct), Ok);
    }

    [HttpGet("trips/{tripId:guid}")]
    [HasPermission(MileagePermissions.View)]
    [RateLimit("mobile.f.staff_trip_read")]
    [ProducesResponseType<StaffTripResponse>(200)]
    public async Task<IActionResult> Trip(Guid customerId, Guid tripId, CancellationToken ct)
    {
        if (await Scope(customerId, ct) is not { } scope)
            return Unauthorized();
        return Respond(await bus.InvokeAsync<Result<StaffTripResponse>>(new StaffGetTripQuery(scope, tripId), ct), Ok);
    }

    [HttpGet("trips/{tripId:guid}/points")]
    [HasPermission(MileagePermissions.View)]
    [RateLimit("mobile.f.staff_trip_read")]
    [ProducesResponseType<TripPointsPage>(200)]
    public async Task<IActionResult> Points(
        Guid customerId,
        Guid tripId,
        CancellationToken ct,
        [FromQuery] long after = -1,
        [FromQuery] int size = 512
    )
    {
        if (await Scope(customerId, ct) is not { } scope)
            return Unauthorized();
        return Respond(
            await bus.InvokeAsync<Result<TripPointsPage>>(new StaffTripPointsQuery(scope, tripId, after, size), ct),
            Ok
        );
    }

    [HttpGet("vehicles")]
    [HasPermission(MileagePermissions.View)]
    [RateLimit("mobile.f.staff_trip_read")]
    [ProducesResponseType<IReadOnlyList<VehicleResponse>>(200)]
    public async Task<IActionResult> Vehicles(Guid customerId, CancellationToken ct)
    {
        if (await Scope(customerId, ct) is not { } scope)
            return Unauthorized();
        return Respond(
            await bus.InvokeAsync<Result<IReadOnlyList<VehicleResponse>>>(new StaffListVehiclesQuery(scope), ct),
            Ok
        );
    }

    [HttpGet("mileage-summary")]
    [HasPermission(MileagePermissions.View)]
    [RateLimit("mobile.f.staff_trip_read")]
    [ProducesResponseType<StaffMileageSummaryResponse>(200)]
    public async Task<IActionResult> Summary(
        Guid customerId,
        CancellationToken ct,
        [FromQuery] int? taxYear = null,
        [FromQuery] Guid? vehicleId = null
    )
    {
        if (await Scope(customerId, ct) is not { } scope)
            return Unauthorized();
        return Respond(
            await bus.InvokeAsync<Result<StaffMileageSummaryResponse>>(
                new StaffMileageSummaryQuery(scope, taxYear, vehicleId),
                ct
            ),
            Ok
        );
    }

    [HttpGet("trips/export")]
    [HasPermission(MileagePermissions.View)]
    [RateLimit("mobile.h.staff_trip_export")]
    [ProducesResponseType<FileContentResult>(200)]
    public async Task<IActionResult> Export(Guid customerId, CancellationToken ct, [FromQuery] int? taxYear = null)
    {
        if (await Scope(customerId, ct) is not { } scope)
            return Unauthorized();
        return Respond(
            await bus.InvokeAsync<Result<StaffTripsExport>>(new StaffExportTripsQuery(scope, taxYear), ct),
            export =>
                File(
                    Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(export.Content)).ToArray(),
                    "text/csv",
                    export.FileName
                )
        );
    }

    private async Task<StaffCustomerScope?> Scope(Guid customerId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return null;
        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        return new StaffCustomerScope(tenantId, userId, canViewAll, customerId);
    }

    private IActionResult Respond<T>(Result<T> result, Func<T, IActionResult> success) =>
        result.IsSuccess ? success(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
}
