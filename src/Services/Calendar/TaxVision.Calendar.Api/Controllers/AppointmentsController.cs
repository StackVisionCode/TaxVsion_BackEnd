using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Calendar.Api.Requests;
using TaxVision.Calendar.Application.Appointments;
using TaxVision.Calendar.Application.Appointments.Commands;
using TaxVision.Calendar.Application.Appointments.Queries;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Calendar.Api.Controllers;

[ApiController]
[Route("calendar/appointments")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class AppointmentsController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Devuelve 201 con <c>warnings</c> cuando el solapamiento sólo avisa, y 409 cuando el tipo de
    /// cita declara que solapar es un error.
    /// </summary>
    [HttpPost]
    [HasPermission(CalendarPermissions.Write)]
    [RateLimit("calendar.g.create")]
    public async Task<IActionResult> Schedule([FromBody] ScheduleAppointmentRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<AppointmentWithWarnings>>(
            new ScheduleAppointmentCommand(
                tenantId,
                userId,
                request.Title,
                request.Description,
                request.Location,
                request.AppointmentTypeId,
                request.TimeZoneId,
                request.StartUtc,
                request.EndUtc,
                request.SeriesStartDate,
                request.LocalStartTime,
                request.Duration,
                request.RecurrenceRule,
                request.CustomerId,
                request.TaxYear,
                request.IsVirtual
            ),
            ct
        );

        return result.IsFailure
            ? StatusCode(result.Error.ToHttpStatusCode(), result.Error)
            : CreatedAtAction(nameof(GetById), new { appointmentId = result.Value.Appointment.Id }, result.Value);
    }

    [HttpGet("{appointmentId:guid}")]
    [HasPermission(CalendarPermissions.Read)]
    [RateLimit("calendar.f.read")]
    public async Task<IActionResult> GetById(Guid appointmentId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();

        var result = await bus.InvokeAsync<Result<AppointmentResponse>>(
            new GetAppointmentByIdQuery(tenantId, appointmentId),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    /// <summary>La consulta que pinta el calendario: expande las series del rango al vuelo.</summary>
    [HttpGet]
    [HasPermission(CalendarPermissions.Read)]
    [RateLimit("calendar.h.range")]
    public async Task<IActionResult> GetRange(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? organizerUserId,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<OccurrenceResponse>>>(
            new GetAppointmentRangeQuery(tenantId, AsUtc(from), AsUtc(to), organizerUserId),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    /// <summary>
    /// Las citas del día de quien pregunta.
    ///
    /// <para>
    /// <paramref name="timeZoneId"/> decide dónde empieza y termina ese día. Sin él el día es el de
    /// UTC, que es lo que hacía antes y por eso sigue siendo el valor por defecto — pero para alguien
    /// en Nueva York eso mete su cena de las 20:30 en la agenda del día siguiente. El frontend conoce
    /// la zona del navegador: debe mandarla.
    /// </para>
    ///
    /// <para><paramref name="date"/> es una fecha (<c>2027-05-10</c>), no un instante.</para>
    /// </summary>
    [HttpGet("my-day")]
    [HasPermission(CalendarPermissions.Read)]
    [RateLimit("calendar.h.range")]
    public async Task<IActionResult> GetMyDay(
        [FromQuery] DateTime? date,
        [FromQuery] string? timeZoneId,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var bounds = DayBounds(date, timeZoneId);
        if (bounds.IsFailure)
            return StatusCode(bounds.Error.ToHttpStatusCode(), bounds.Error);

        var result = await bus.InvokeAsync<Result<IReadOnlyList<OccurrenceResponse>>>(
            new GetAppointmentRangeQuery(tenantId, bounds.Value.StartUtc, bounds.Value.EndUtc, userId),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    /// <summary>
    /// De dónde a dónde va «el día». Con zona se calcula de medianoche a medianoche <b>de esa zona</b>
    /// y con el motor único, que además corre hacia adelante la medianoche que no existe — la hay, en
    /// las zonas que cambian el horario justo a las 00:00.
    /// </summary>
    private static Result<(DateTime StartUtc, DateTime EndUtc)> DayBounds(DateTime? date, string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            var utcDay = AsUtc(date ?? DateTime.UtcNow).Date;
            return Result.Success(
                (
                    DateTime.SpecifyKind(utcDay, DateTimeKind.Utc),
                    DateTime.SpecifyKind(utcDay.AddDays(1), DateTimeKind.Utc)
                )
            );
        }

        var zone = CalendarTimeZone.Create(timeZoneId);
        if (zone.IsFailure)
            return Result.Failure<(DateTime, DateTime)>(zone.Error);

        // Sin fecha, «hoy» tambien es el de su zona: en Nueva York a las 21:00 todavia es ayer en UTC.
        var today = WallClock.ToWallClock(DateTime.UtcNow, zone.Value);
        if (today.IsFailure)
            return Result.Failure<(DateTime, DateTime)>(today.Error);

        var day = DateOnly.FromDateTime(date?.Date ?? today.Value);

        var start = WallClock.ToUtcShiftingOverGaps(day, TimeOnly.MinValue, zone.Value);
        if (start.IsFailure)
            return Result.Failure<(DateTime, DateTime)>(start.Error);

        var end = WallClock.ToUtcShiftingOverGaps(day.AddDays(1), TimeOnly.MinValue, zone.Value);
        return end.IsFailure
            ? Result.Failure<(DateTime, DateTime)>(end.Error)
            : Result.Success((start.Value, end.Value));
    }

    /// <summary>
    /// Sobre una serie, <c>scope</c> es obligatorio: sin él se responde 400. Elegirlo en silencio
    /// reescribe el pasado o frustra a quien quería mover todo.
    /// </summary>
    [HttpPut("{appointmentId:guid}/schedule")]
    [HasPermission(CalendarPermissions.Write)]
    [RateLimit("calendar.g.update")]
    public async Task<IActionResult> Reschedule(
        Guid appointmentId,
        [FromBody] RescheduleAppointmentRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<AppointmentResponse>>(
            new RescheduleAppointmentCommand(
                tenantId,
                appointmentId,
                userId,
                request.Scope,
                request.OriginalStartUtc,
                request.NewStartUtc,
                request.NewEndUtc,
                request.SeriesStartDate,
                request.LocalStartTime,
                request.Duration,
                request.TimeZoneId,
                request.RecurrenceRule
            ),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpPost("{appointmentId:guid}/cancel")]
    [HasPermission(CalendarPermissions.Write)]
    [RateLimit("calendar.g.delete")]
    public async Task<IActionResult> Cancel(
        Guid appointmentId,
        [FromBody] CancelAppointmentRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(
            new CancelAppointmentCommand(
                tenantId,
                appointmentId,
                userId,
                request.Scope,
                request.OriginalStartUtc,
                request.Reason
            ),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : NoContent();
    }

    [HttpPost("{appointmentId:guid}/attendees")]
    [HasPermission(CalendarPermissions.Write)]
    [RateLimit("calendar.g.update")]
    public async Task<IActionResult> AddAttendee(
        Guid appointmentId,
        [FromBody] AddAttendeeRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<AppointmentResponse>>(
            new AddAttendeeCommand(
                tenantId,
                appointmentId,
                userId,
                request.Kind,
                request.UserId,
                request.CustomerId,
                request.DisplayName,
                request.Email,
                request.IsRequired
            ),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpDelete("{appointmentId:guid}/attendees/{attendeeId:guid}")]
    [HasPermission(CalendarPermissions.Write)]
    [RateLimit("calendar.g.update")]
    public async Task<IActionResult> RemoveAttendee(Guid appointmentId, Guid attendeeId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(
            new RemoveAttendeeCommand(tenantId, appointmentId, attendeeId, userId),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : NoContent();
    }

    /// <summary>Lo único que un asistente puede hacer sin ser organizador.</summary>
    [HttpPost("{appointmentId:guid}/respond")]
    [HasPermission(CalendarPermissions.Read)]
    [RateLimit("calendar.g.rsvp")]
    public async Task<IActionResult> Respond(
        Guid appointmentId,
        [FromBody] RespondToAppointmentRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(
            new RespondToAppointmentCommand(tenantId, appointmentId, userId, request.Response),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : NoContent();
    }

    /// <summary>
    /// El query string no lleva zona, así que un <c>DateTime</c> sin <c>Z</c> llega como
    /// <c>Unspecified</c> y el dominio lo rechaza. Se interpreta como UTC, que es lo que documenta la
    /// API.
    /// </summary>
    /// <summary>
    /// Medido: el binder de ASP.NET Core ya convierte un offset a UTC, asi que aca solo llega
    /// <c>Unspecified</c> —una fecha sin zona— y declararla UTC es la convencion de la API.
    /// </summary>
    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
