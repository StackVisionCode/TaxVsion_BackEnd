using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Calendar.Api.Requests;
using TaxVision.Calendar.Application.Appointments.Search;
using TaxVision.Calendar.Application.Portal;
using Wolverine;

namespace TaxVision.Calendar.Api.Controllers;

/// <summary>
/// El calendario del cliente en el portal (B6) y su reserva (B7). Solo el actor CustomerPortal; el
/// cliente sale siempre del token, nunca de la URL ni del cuerpo. Una cita ajena es 404.
/// </summary>
[ApiController]
[Route("calendar/portal")]
[AllowActorTypes(ActorType.CustomerPortal)]
public sealed class PortalCalendarController(IMessageBus bus) : ControllerBase
{
    private const string IdempotencyHeader = "Idempotency-Key";

    [HttpGet("appointments")]
    [HasPermission(CalendarPermissions.PortalRead)]
    [RateLimit("calendar.f.portal_read")]
    public async Task<IActionResult> List([FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
    {
        if (!TryGetClient(out var tenantId, out _, out var customerId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<PortalAppointmentResponse>>>(
            new ListPortalAppointmentsQuery(tenantId, customerId, Utc(from), Utc(to)),
            ct
        );
        return Reply(result);
    }

    /// <summary>Busca entre las citas del cliente por título o lugar, con filtro de estado.</summary>
    [HttpGet("appointments/search")]
    [HasPermission(CalendarPermissions.PortalRead)]
    [RateLimit("calendar.h.search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] AppointmentSearchStatus status,
        [FromQuery] int take,
        CancellationToken ct
    )
    {
        if (!TryGetClient(out var tenantId, out _, out var customerId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<AppointmentSearchResult>>(
            new SearchPortalAppointmentsQuery(tenantId, customerId, q, status, take),
            ct
        );
        return Reply(result);
    }

    [HttpGet("appointments/{appointmentId:guid}")]
    [HasPermission(CalendarPermissions.PortalRead)]
    [RateLimit("calendar.f.portal_read")]
    public async Task<IActionResult> Get(Guid appointmentId, CancellationToken ct)
    {
        if (!TryGetClient(out var tenantId, out _, out var customerId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<PortalAppointmentResponse>>(
            new GetPortalAppointmentQuery(tenantId, customerId, appointmentId),
            ct
        );
        return Reply(result);
    }

    [HttpPost("appointments/{appointmentId:guid}/respond")]
    [HasPermission(CalendarPermissions.PortalRead)]
    [RateLimit("calendar.g.portal_book")]
    public async Task<IActionResult> Respond(
        Guid appointmentId,
        [FromBody] PortalRespondRequest request,
        CancellationToken ct
    )
    {
        if (!TryGetClient(out var tenantId, out _, out var customerId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(
            new RespondAsCustomerCommand(tenantId, customerId, appointmentId, request.Response),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : NoContent();
    }

    [HttpGet("booking/options")]
    [HasPermission(CalendarPermissions.PortalBook)]
    [RateLimit("calendar.f.portal_read")]
    public async Task<IActionResult> Options(CancellationToken ct)
    {
        if (!TryGetClient(out var tenantId, out _, out var customerId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<BookingOptionsResponse>>(
            new GetBookingOptionsQuery(tenantId, customerId),
            ct
        );
        return Reply(result);
    }

    [HttpGet("booking/slots")]
    [HasPermission(CalendarPermissions.PortalBook)]
    [RateLimit("calendar.f.portal_read")]
    public async Task<IActionResult> Slots(
        [FromQuery] Guid typeId,
        [FromQuery] Guid? preparerUserId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? rescheduleAppointmentId,
        CancellationToken ct
    )
    {
        if (!TryGetClient(out var tenantId, out _, out var customerId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<BookingSlotsResponse>>(
            new GetBookingSlotsQuery(
                tenantId,
                customerId,
                typeId,
                preparerUserId,
                Utc(from),
                Utc(to),
                rescheduleAppointmentId
            ),
            ct
        );
        return Reply(result);
    }

    /// <summary>Exige <c>Idempotency-Key</c>: un doble clic o un reintento devuelve la misma cita.</summary>
    [HttpPost("bookings")]
    [HasPermission(CalendarPermissions.PortalBook)]
    [RateLimit("calendar.g.portal_book")]
    public async Task<IActionResult> Book([FromBody] PortalBookRequest request, CancellationToken ct)
    {
        if (!TryGetClient(out var tenantId, out var userId, out var customerId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<PortalAppointmentResponse>>(
            new BookAsCustomerCommand(
                tenantId,
                customerId,
                userId,
                User.FindFirst("name")?.Value,
                User.TryGetEmail(out var email) ? email : null,
                Request.Headers[IdempotencyHeader].ToString(),
                request.TypeId,
                request.PreparerUserId,
                Utc(request.StartUtc)
            ),
            ct
        );
        return result.IsFailure
            ? StatusCode(result.Error.ToHttpStatusCode(), result.Error)
            : StatusCode(201, result.Value);
    }

    [HttpPost("appointments/{appointmentId:guid}/cancel")]
    [HasPermission(CalendarPermissions.PortalBook)]
    [RateLimit("calendar.g.portal_book")]
    public async Task<IActionResult> Cancel(
        Guid appointmentId,
        [FromBody] PortalCancelRequest request,
        CancellationToken ct
    )
    {
        if (!TryGetClient(out var tenantId, out var userId, out var customerId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(
            new CancelAsCustomerCommand(tenantId, customerId, userId, appointmentId, request.Reason),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : NoContent();
    }

    [HttpPost("appointments/{appointmentId:guid}/reschedule")]
    [HasPermission(CalendarPermissions.PortalBook)]
    [RateLimit("calendar.g.portal_book")]
    public async Task<IActionResult> Reschedule(
        Guid appointmentId,
        [FromBody] PortalRescheduleRequest request,
        CancellationToken ct
    )
    {
        if (!TryGetClient(out var tenantId, out var userId, out var customerId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<PortalAppointmentResponse>>(
            new RescheduleAsCustomerCommand(tenantId, customerId, userId, appointmentId, Utc(request.StartUtc)),
            ct
        );
        return Reply(result);
    }

    private bool TryGetClient(out Guid tenantId, out Guid userId, out Guid customerId)
    {
        customerId = Guid.Empty;
        return this.TryGetTenantAndUser(out tenantId, out userId) && User.TryGetCustomerId(out customerId);
    }

    private IActionResult Reply<T>(Result<T> result) =>
        result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);

    private static DateTime Utc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
}
