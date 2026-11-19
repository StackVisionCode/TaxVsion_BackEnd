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
using TaxVision.Mobile.Application.Vehicles;
using TaxVision.Mobile.Application.Vehicles.Commands;
using TaxVision.Mobile.Application.Vehicles.Queries;
using Wolverine;

namespace TaxVision.Mobile.Api.Controllers;

[ApiController]
[Authorize]
[AllowActorTypes(ActorType.CustomerPortal)]
[Route("mobile/vehicles")]
[Produces("application/json")]
public sealed class VehiclesController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    [HasPermission(MobilePermissions.VehiclesManage)]
    [RateLimit("mobile.g.vehicle_write")]
    [ProducesResponseType<VehicleResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(VehicleInput request, CancellationToken ct)
    {
        if (!Identity(out var tenant, out var customer))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result<VehicleResponse>>(
            new CreateVehicleCommand(tenant, customer, request),
            ct
        );
        return Respond(result, v => CreatedAtAction(nameof(GetById), new { id = v.Id }, v));
    }

    [HttpGet("{id:guid}")]
    [HasPermission(MobilePermissions.VehiclesRead)]
    [RateLimit("mobile.f.vehicle_read")]
    [ProducesResponseType<VehicleResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!Identity(out var tenant, out var customer))
            return Unauthorized();
        return Respond(
            await bus.InvokeAsync<Result<VehicleResponse>>(new GetVehicleQuery(tenant, customer, id), ct),
            Ok
        );
    }

    [HttpGet]
    [HasPermission(MobilePermissions.VehiclesRead)]
    [RateLimit("mobile.f.vehicle_read")]
    [ProducesResponseType<PagedResult<VehicleResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int size = 50)
    {
        if (!Identity(out var tenant, out var customer))
            return Unauthorized();
        return Respond(
            await bus.InvokeAsync<Result<PagedResult<VehicleResponse>>>(
                new ListVehiclesQuery(tenant, customer, page, size),
                ct
            ),
            Ok
        );
    }

    [HttpPut("{id:guid}")]
    [HasPermission(MobilePermissions.VehiclesManage)]
    [RateLimit("mobile.g.vehicle_write")]
    [ProducesResponseType<VehicleResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, VehicleInput request, CancellationToken ct)
    {
        if (!Identity(out var tenant, out var customer))
            return Unauthorized();
        return Respond(
            await bus.InvokeAsync<Result<VehicleResponse>>(new UpdateVehicleCommand(tenant, customer, id, request), ct),
            Ok
        );
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(MobilePermissions.VehiclesManage)]
    [RateLimit("mobile.g.vehicle_write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!Identity(out var tenant, out var customer))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result>(new DeleteVehicleCommand(tenant, customer, id), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private bool Identity(out Guid tenantId, out Guid customerId)
    {
        Response.Headers.CacheControl = "no-store";
        customerId = Guid.Empty;
        return this.TryGetTenantAndUser(out tenantId, out _)
            && User.TryGetCustomerId(out customerId)
            && customerId != Guid.Empty;
    }

    private IActionResult Respond<T>(Result<T> result, Func<T, IActionResult> success) =>
        result.IsSuccess ? success(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
}
