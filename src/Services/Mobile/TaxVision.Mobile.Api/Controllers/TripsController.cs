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
[AllowActorTypes(ActorType.CustomerPortal)]
[Route("mobile/trips")]
[Produces("application/json")]
public sealed class TripsController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    [HasPermission(MobilePermissions.TripsManage)]
    [RateLimit("mobile.g.trip_write")]
    [ProducesResponseType<TripResponse>(201)]
    [ProducesResponseType<TripResponse>(200)]
    public async Task<IActionResult> Start(StartTripInput request, CancellationToken ct)
    {
        if (!Identity(out var t, out var c))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result<TripResponse>>(new StartTripCommand(t, c, request), ct);
        return Respond(result, x => x.Replayed ? Ok(x) : CreatedAtAction(nameof(Get), new { id = x.Id }, x));
    }

    [HttpGet]
    [HasPermission(MobilePermissions.TripsRead)]
    [RateLimit("mobile.f.trip_read")]
    [ProducesResponseType<PagedResult<TripResponse>>(200)]
    public async Task<IActionResult> List([FromQuery] TripFilter filter, CancellationToken ct)
    {
        if (!Identity(out var t, out var c))
            return Unauthorized();
        return Respond(
            await bus.InvokeAsync<Result<PagedResult<TripResponse>>>(new ListTripsQuery(t, c, filter), ct),
            Ok
        );
    }

    [HttpGet("active")]
    [HasPermission(MobilePermissions.TripsRead)]
    [RateLimit("mobile.f.trip_read")]
    [ProducesResponseType<IReadOnlyList<TripResponse>>(200)]
    public async Task<IActionResult> Active(CancellationToken ct)
    {
        if (!Identity(out var t, out var c))
            return Unauthorized();
        return Ok(await bus.InvokeAsync<IReadOnlyList<TripResponse>>(new ActiveTripsQuery(t, c), ct));
    }

    [HttpGet("{id:guid}")]
    [HasPermission(MobilePermissions.TripsRead)]
    [RateLimit("mobile.f.trip_read")]
    [ProducesResponseType<TripResponse>(200)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!Identity(out var t, out var c))
            return Unauthorized();
        return Respond(await bus.InvokeAsync<Result<TripResponse>>(new GetTripQuery(t, c, id), ct), Ok);
    }

    [HttpGet("{id:guid}/points")]
    [HasPermission(MobilePermissions.TripsRead)]
    [RateLimit("mobile.f.trip_read")]
    [ProducesResponseType<TripPointsPage>(200)]
    public async Task<IActionResult> Points(
        Guid id,
        CancellationToken ct,
        [FromQuery] long after = -1,
        [FromQuery] int size = 100
    )
    {
        if (!Identity(out var t, out var c))
            return Unauthorized();
        return Respond(
            await bus.InvokeAsync<Result<TripPointsPage>>(new TripPointsQuery(t, c, id, after, size), ct),
            Ok
        );
    }

    [HttpPut("{id:guid}/tracking")]
    [HasPermission(MobilePermissions.TripsSync)]
    [RateLimit("mobile.g.trip_sync")]
    [RequestSizeLimit(262144)]
    [ProducesResponseType<TripResponse>(200)]
    public Task<IActionResult> Tracking(Guid id, TrackingInput request, CancellationToken ct) =>
        Change(id, TripAction.Tracking, request.Sequence, request.ObservedAt, ct, request.Points);

    [HttpPost("{id:guid}/pause")]
    [HasPermission(MobilePermissions.TripsSync)]
    [RateLimit("mobile.g.trip_sync")]
    [ProducesResponseType<TripResponse>(200)]
    public Task<IActionResult> Pause(Guid id, TransitionInput request, CancellationToken ct) =>
        Change(id, TripAction.Pause, request.Sequence, request.ObservedAt, ct);

    [HttpPost("{id:guid}/resume")]
    [HasPermission(MobilePermissions.TripsSync)]
    [RateLimit("mobile.g.trip_sync")]
    [ProducesResponseType<TripResponse>(200)]
    public Task<IActionResult> Resume(Guid id, TransitionInput request, CancellationToken ct) =>
        Change(id, TripAction.Resume, request.Sequence, request.ObservedAt, ct);

    [HttpPost("{id:guid}/complete")]
    [HasPermission(MobilePermissions.TripsSync)]
    [RateLimit("mobile.g.trip_sync")]
    [RequestSizeLimit(262144)]
    [ProducesResponseType<TripResponse>(200)]
    public Task<IActionResult> Complete(Guid id, TrackingInput request, CancellationToken ct) =>
        Change(id, TripAction.Complete, request.Sequence, request.ObservedAt, ct, request.Points);

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(MobilePermissions.TripsSync)]
    [RateLimit("mobile.g.trip_sync")]
    [ProducesResponseType<TripResponse>(200)]
    public Task<IActionResult> Cancel(Guid id, TransitionInput request, CancellationToken ct) =>
        Change(id, TripAction.Cancel, request.Sequence, request.ObservedAt, ct);

    [HttpPatch("{id:guid}")]
    [HasPermission(MobilePermissions.TripsManage)]
    [RateLimit("mobile.g.trip_write")]
    [ProducesResponseType<TripResponse>(200)]
    public Task<IActionResult> Edit(Guid id, EditTripInput request, CancellationToken ct) =>
        Change(id, TripAction.Edit, request.Sequence, request.ObservedAt, ct, null, request.Purpose, request.Notes);

    [HttpDelete("{id:guid}")]
    [HasPermission(MobilePermissions.TripsManage)]
    [RateLimit("mobile.g.trip_write")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromQuery] long sequence,
        [FromQuery] DateTimeOffset observedAt,
        CancellationToken ct
    )
    {
        if (!Identity(out var t, out var c))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result<TripResponse>>(
            new ChangeTripCommand(t, c, id, TripAction.Delete, sequence, observedAt),
            ct
        );
        return Respond(result, _ => NoContent());
    }

    private async Task<IActionResult> Change(
        Guid id,
        TripAction action,
        long sequence,
        DateTimeOffset at,
        CancellationToken ct,
        IReadOnlyList<GpsPointInput>? points = null,
        string? purpose = null,
        string? notes = null
    )
    {
        if (!Identity(out var t, out var c))
            return Unauthorized();
        return Respond(
            await bus.InvokeAsync<Result<TripResponse>>(
                new ChangeTripCommand(t, c, id, action, sequence, at, points, purpose, notes),
                ct
            ),
            Ok
        );
    }

    private bool Identity(out Guid tenant, out Guid customer)
    {
        Response.Headers.CacheControl = "no-store";
        customer = Guid.Empty;
        return this.TryGetTenantAndUser(out tenant, out _)
            && User.TryGetCustomerId(out customer)
            && customer != Guid.Empty;
    }

    private IActionResult Respond<T>(Result<T> result, Func<T, IActionResult> success) =>
        result.IsSuccess ? success(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
}
