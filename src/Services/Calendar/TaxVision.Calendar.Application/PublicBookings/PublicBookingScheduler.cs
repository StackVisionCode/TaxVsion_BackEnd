using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Appointments.Commands;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Availability.Queries;
using TaxVision.Calendar.Application.Office.Abstractions;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Booking;
using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.Types;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Application.PublicBookings;

/// <summary>
/// La única fuente de "qué horario se puede reservar" en una página pública: la lista, la retención, la
/// confirmación y el cambio de hora usan esto mismo. Los horarios salen de la disponibilidad de cada
/// anfitrión (como en el portal) menos las retenciones vigentes de otros visitantes.
/// </summary>
public sealed class PublicBookingScheduler(
    IAppointmentRepository appointments,
    IAvailabilityRepository availability,
    IAppointmentTypeRepository types,
    IOfficeScheduleRepository offices,
    IOrganizerScheduleLock scheduleLock,
    IPublicBookingRepository bookings,
    IUnitOfWork unitOfWork
)
{
    /// <summary>El tipo de la página, si sigue usable (activo, presencial y bloqueante).</summary>
    public async Task<AppointmentType?> TypeOfAsync(BookingPage page, CancellationToken ct)
    {
        var type = await types.GetByIdAsync(page.TenantId, page.AppointmentTypeId, ct);
        return type.IsSuccess && BookingPage.IsUsableType(type.Value) ? type.Value : null;
    }

    /// <summary>Los comienzos ofrecidos en el rango con cualquier anfitrión, sin repetir y en orden.</summary>
    public async Task<IReadOnlyList<DateTime>> ListStartsAsync(
        BookingPage page,
        AppointmentType type,
        DateTime fromUtc,
        DateTime toUtc,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var holds = await bookings.ListHoldsAsync(page.TenantId, fromUtc.AddDays(-1), toUtc.AddDays(1), nowUtc, ct);
        var starts = new SortedSet<DateTime>();
        foreach (var host in page.ActiveHosts)
        {
            foreach (
                var start in await HostStartsAsync(
                    page,
                    type,
                    host.UserId,
                    fromUtc,
                    toUtc,
                    nowUtc,
                    holds,
                    null,
                    null,
                    ct
                )
            )
                starts.Add(start);
        }
        return [.. starts];
    }

    /// <summary>
    /// Toma el horario con un anfitrión: el preferido si está libre, si no el primero libre. Bloquea a cada
    /// anfitrión en el orden estable de la página (dos solicitudes nunca se esperan en cruz) y revisa con el
    /// candado tomado. Null si nadie lo cubre.
    /// </summary>
    public async Task<Guid?> ClaimHostAsync(
        BookingPage page,
        AppointmentType type,
        DateTime startUtc,
        Guid? preferredHostId,
        Guid? excludeBookingId,
        Guid? excludeAppointmentId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        Guid? firstFree = null;
        foreach (var host in page.ActiveHosts)
        {
            await scheduleLock.AcquireAsync(page.TenantId, host.UserId, ct);
            var holds = await bookings.ListHoldsAsync(
                page.TenantId,
                startUtc.AddDays(-1),
                startUtc.AddDays(1),
                nowUtc,
                ct
            );
            var starts = await HostStartsAsync(
                page,
                type,
                host.UserId,
                startUtc.AddHours(-12),
                startUtc.AddHours(12),
                nowUtc,
                holds,
                excludeBookingId,
                excludeAppointmentId,
                ct
            );
            if (!starts.Contains(startUtc))
                continue;
            if (host.UserId == preferredHostId)
                return await ReleaseStaleHoldsAsync(page.TenantId, host.UserId, startUtc, nowUtc, ct);
            firstFree ??= host.UserId;
        }
        return firstFree is { } chosen
            ? await ReleaseStaleHoldsAsync(page.TenantId, chosen, startUtc, nowUtc, ct)
            : null;
    }

    /// <summary>
    /// Cierra las retenciones ya vencidas del anfitrión en ese horario. El índice único las sigue contando hasta
    /// que alguien las cierre, y la nueva chocaría con ellas. Se guarda ya, con el candado tomado: el orden de las
    /// sentencias de un mismo SaveChanges no está garantizado.
    /// </summary>
    private async Task<Guid> ReleaseStaleHoldsAsync(
        Guid tenantId,
        Guid hostId,
        DateTime startUtc,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var stale = await bookings.ListExpiredHoldsAsync(tenantId, hostId, startUtc, nowUtc, ct);
        foreach (var hold in stale)
            hold.Expire(nowUtc);
        if (stale.Count > 0)
            await unitOfWork.SaveChangesAsync(ct);
        return hostId;
    }

    /// <summary>Crea la cita del anfitrión con el visitante y confirma la reserva. Devuelve el token de gestión.</summary>
    public async Task<Result<string>> ConfirmAsync(
        BookingPage page,
        PublicBooking booking,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var type = await TypeOfAsync(page, ct);
        if (type is null)
            return Result.Failure<string>(PublicBookingErrors.NotFound);
        var hostId = await ClaimHostAsync(
            page,
            type,
            booking.SlotStartUtc,
            booking.HostUserId,
            booking.Id,
            null,
            nowUtc,
            ct
        );
        if (hostId is not { } host || page.FindActiveHost(host) is not { } pageHost)
            return Result.Failure<string>(PublicBookingErrors.SlotTaken);

        var appointment = await BuildAppointmentAsync(page, booking, pageHost, type, nowUtc, ct);
        if (appointment.IsFailure)
            return Result.Failure<string>(appointment.Error);
        var conflicts = await ConflictProbe.CheckAsync(appointment.Value, type, appointments, availability, ct);
        if (conflicts.IsFailure)
            return Result.Failure<string>(PublicBookingErrors.SlotTaken);

        // La reserva primero: si no puede confirmarse, la cita no se agrega y nada cambia.
        var token = booking.Confirm(host, appointment.Value.Id, nowUtc);
        if (token.IsSuccess)
            appointments.Add(appointment.Value);
        return token;
    }

    /// <summary>
    /// Mueve la demo confirmada (la reserva y su cita juntas). Sin <paramref name="staffUserId"/> la mueve el
    /// visitante; con él, el equipo, y queda su motivo. Devuelve el token de gestión nuevo.
    /// </summary>
    public async Task<Result<string>> MoveAsync(
        BookingPage page,
        PublicBooking booking,
        Appointment appointment,
        DateTime startUtc,
        DateTime nowUtc,
        CancellationToken ct,
        Guid? staffUserId = null,
        string? reason = null
    )
    {
        // Las dos se revisan antes de tocar ninguna: Wolverine guarda aunque el handler devuelva un error.
        var canChange = appointment.CanChangeFromPublicPage();
        if (canChange.IsFailure)
            return Result.Failure<string>(canChange.Error);
        var bookingCanChange = booking.CanBeChanged(nowUtc);
        if (bookingCanChange.IsFailure)
            return Result.Failure<string>(bookingCanChange.Error);
        var type = await TypeOfAsync(page, ct);
        if (type is null)
            return Result.Failure<string>(PublicBookingErrors.NotFound);
        var hostId = await ClaimHostAsync(
            page,
            type,
            startUtc,
            booking.HostUserId,
            booking.Id,
            appointment.Id,
            nowUtc,
            ct
        );
        if (hostId is not { } host || page.FindActiveHost(host) is not { } pageHost)
            return Result.Failure<string>(PublicBookingErrors.SlotTaken);

        var zone = await TimeZoneOfAsync(page.TenantId, host, booking.VisitorTimeZone, ct);
        var timing = EventTiming.PointInTimeOf(startUtc, startUtc.AddMinutes(booking.DurationMinutes), zone);
        var location = Location.Create(pageHost.MeetingUrl);
        if (timing.IsFailure || location.IsFailure)
            return Result.Failure<string>(PublicBookingErrors.SlotTaken);

        var token = staffUserId is { } staff
            ? booking.RescheduleByStaff(startUtc, host, reason, nowUtc)
            : booking.RescheduleByVisitor(startUtc, host, nowUtc);
        if (token.IsFailure)
            return token;
        var moved = staffUserId is { } actor
            ? appointment.RescheduleByPublicPageStaff(timing.Value, actor, nowUtc, host, location.Value)
            : appointment.RescheduleByExternalRequester(timing.Value, nowUtc, host, location.Value);
        return moved.IsFailure ? Result.Failure<string>(moved.Error) : token;
    }

    private async Task<Result<Appointment>> BuildAppointmentAsync(
        BookingPage page,
        PublicBooking booking,
        BookingPageHost host,
        AppointmentType type,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var zone = await TimeZoneOfAsync(page.TenantId, host.UserId, booking.VisitorTimeZone, ct);
        var timing = EventTiming.PointInTimeOf(
            booking.SlotStartUtc,
            booking.SlotStartUtc.AddMinutes(booking.DurationMinutes),
            zone
        );
        var visitor = AttendeeSnapshot.Create(booking.Name, booking.Email);
        var location = Location.Create(host.MeetingUrl);
        var title = AppointmentTitle.Create(Truncate($"{type.Name} · {booking.Company}", AppointmentTitle.MaxLength));
        if (timing.IsFailure || visitor.IsFailure || location.IsFailure || title.IsFailure)
            return Result.Failure<Appointment>(PublicBookingErrors.RequestInvalid);

        // Sin el anfitrión como asistente: sus avisos son los de la demo, no los genéricos de la cita.
        return Appointment.Schedule(
            page.TenantId,
            title.Value,
            timing.Value,
            type.Id,
            host.UserId,
            nowUtc,
            description: "Booked online from the public demo page.",
            location: location.Value,
            attendees:
            [
                new InitialAttendee(AttendeeKind.External, null, null, visitor.Value, IsRequired: true, Accepted: true),
            ],
            bookingPageId: page.Id
        );
    }

    /// <summary>Zona de la cita: la de la disponibilidad del anfitrión; sin ella, la del visitante.</summary>
    private async Task<string> TimeZoneOfAsync(Guid tenantId, Guid hostId, string visitorZone, CancellationToken ct)
    {
        foreach (var rule in await availability.ListRulesAsync(tenantId, hostId, ct))
        {
            if (rule.IsActive)
                return rule.TimeZone.Id;
        }
        return visitorZone;
    }

    private async Task<List<DateTime>> HostStartsAsync(
        BookingPage page,
        AppointmentType type,
        Guid hostId,
        DateTime fromUtc,
        DateTime toUtc,
        DateTime nowUtc,
        IReadOnlyList<PublicBooking> holds,
        Guid? excludeBookingId,
        Guid? excludeAppointmentId,
        CancellationToken ct
    )
    {
        var from = Max(fromUtc, nowUtc.AddMinutes(page.MinNoticeMinutes));
        var to = Min(toUtc, nowUtc.AddDays(page.MaxDaysAhead));
        if (to <= from)
            return [];

        var free = await GetAvailabilityHandler.Handle(
            new GetAvailabilityQuery(page.TenantId, hostId, from, to, type.Id)
            {
                BufferMinutes = page.BufferMinutes,
                ExcludeAppointmentId = excludeAppointmentId,
            },
            appointments,
            availability,
            types,
            offices,
            ct
        );
        if (free.IsFailure)
            return [];

        var windows = new List<TimeSlot>();
        foreach (var slot in free.Value)
            windows.Add(new TimeSlot(slot.StartUtc, slot.EndUtc));

        var starts = new List<DateTime>();
        foreach (var slot in BookingSlots.Plan(windows, type.DefaultDuration, page.SlotStepMinutes))
        {
            if (!IsHeld(slot, hostId, holds, excludeBookingId, page.BufferMinutes))
                starts.Add(slot.StartUtc);
        }
        return starts;
    }

    /// <summary>Otro visitante retiene ese horario (con el margen de la página) del mismo anfitrión.</summary>
    private static bool IsHeld(
        TimeSlot slot,
        Guid hostId,
        IReadOnlyList<PublicBooking> holds,
        Guid? excludeBookingId,
        int bufferMinutes
    )
    {
        foreach (var hold in holds)
        {
            if (hold.HostUserId != hostId || hold.Id == excludeBookingId)
                continue;
            var heldFrom = hold.SlotStartUtc.AddMinutes(-bufferMinutes);
            var heldTo = hold.SlotStartUtc.AddMinutes(hold.DurationMinutes + bufferMinutes);
            if (slot.StartUtc < heldTo && slot.EndUtc > heldFrom)
                return true;
        }
        return false;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
