using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.Types;

namespace TaxVision.Calendar.Application.Appointments.Commands;

/// <summary>
/// Comprueba el hueco del organizador antes de guardar.
///
/// <para>
/// Devuelve la lista de avisos cuando el solapamiento no bloquea, y falla con
/// <c>Calendar.Appointment.Conflict</c> cuando sí — que la capa web mapea a 409.
/// </para>
/// </summary>
internal static class ConflictProbe
{
    public static async Task<Result<IReadOnlyList<string>>> CheckAsync(
        Appointment appointment,
        AppointmentType type,
        IAppointmentRepository appointments,
        IAvailabilityRepository availability,
        CancellationToken ct
    )
    {
        var (fromUtc, toUtc) = Window(appointment);

        var occurrences = OccurrenceExpander.Expand(appointment, fromUtc, toUtc);
        if (occurrences.IsFailure)
            return Result.Failure<IReadOnlyList<string>>(occurrences.Error);

        var slots = occurrences.Value.Select(o => (o.StartUtc, o.EndUtc)).ToList();
        return await CheckSlotsAsync(appointment, slots, fromUtc, toUtc, type, appointments, availability, ct);
    }

    /// <summary>Solo la agenda de quien se suma a una cita existente: la del organizador ya se revisó al agendar.</summary>
    public static async Task<Result<IReadOnlyList<string>>> CheckAttendeeAsync(
        Appointment appointment,
        Guid attendeeUserId,
        AppointmentType type,
        IAppointmentRepository appointments,
        IAvailabilityRepository availability,
        CancellationToken ct
    )
    {
        var (fromUtc, toUtc) = Window(appointment);
        var occurrences = OccurrenceExpander.Expand(appointment, fromUtc, toUtc);
        if (occurrences.IsFailure)
            return Result.Failure<IReadOnlyList<string>>(occurrences.Error);

        var slots = occurrences.Value.Select(o => (o.StartUtc, o.EndUtc)).ToList();
        var existing = await appointments.ListForRangeAsync(appointment.TenantId, fromUtc, toUtc, ct);
        var people = await PeopleWarningsAsync(
            appointment,
            slots,
            existing,
            fromUtc,
            toUtc,
            type,
            availability,
            ct,
            attendeeUserId
        );
        return people.IsFailure
            ? Result.Failure<IReadOnlyList<string>>(people.Error)
            : Result.Success<IReadOnlyList<string>>(people.Value);
    }

    /// <summary>Solo esta franja: mover una ocurrencia no responde por el resto de la serie.</summary>
    public static Task<Result<IReadOnlyList<string>>> CheckSlotAsync(
        Appointment appointment,
        DateTime startUtc,
        DateTime endUtc,
        AppointmentType type,
        IAppointmentRepository appointments,
        IAvailabilityRepository availability,
        CancellationToken ct
    ) => CheckSlotsAsync(appointment, [(startUtc, endUtc)], startUtc, endUtc, type, appointments, availability, ct);

    private static async Task<Result<IReadOnlyList<string>>> CheckSlotsAsync(
        Appointment appointment,
        IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)> slots,
        DateTime fromUtc,
        DateTime toUtc,
        AppointmentType type,
        IAppointmentRepository appointments,
        IAvailabilityRepository availability,
        CancellationToken ct
    )
    {
        var existing = await appointments.ListForRangeAsync(appointment.TenantId, fromUtc, toUtc, ct);
        var busy = Busy(existing, appointment, appointment.OrganizerUserId, fromUtc, toUtc);
        var blocks = await availability.ListBlocksAsync(
            appointment.TenantId,
            appointment.OrganizerUserId,
            fromUtc,
            toUtc,
            ct
        );

        var warnings = new List<string>();
        foreach (var (startUtc, endUtc) in slots)
        {
            var result = ConflictDetector.Check(startUtc, endUtc, type, busy, blocks);
            if (!result.HasConflict)
                continue;

            if (result.Blocks)
                return Result.Failure<IReadOnlyList<string>>(AppointmentErrors.Conflict);

            warnings.Add($"Overlaps another appointment on {startUtc:yyyy-MM-dd HH:mm} UTC.");
        }

        var people = await PeopleWarningsAsync(appointment, slots, existing, fromUtc, toUtc, type, availability, ct);
        if (people.IsFailure)
            return Result.Failure<IReadOnlyList<string>>(people.Error);
        warnings.AddRange(people.Value);

        var cap = await DailyCapAsync(appointment, slots, type, appointments, ct);
        return cap.IsFailure
            ? Result.Failure<IReadOnlyList<string>>(cap.Error)
            : Result.Success<IReadOnlyList<string>>(warnings);
    }

    /// <summary>
    /// La agenda de cada invitado del equipo (no el organizador, ya revisado). Un solape avisa con su
    /// nombre, o bloquea si el tipo no admite solapes; su tiempo libre (vacaciones) solo avisa: no es
    /// la agenda del organizador para decidir por él. Un aviso por persona alcanza.
    /// </summary>
    private static async Task<Result<List<string>>> PeopleWarningsAsync(
        Appointment appointment,
        IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)> slots,
        IReadOnlyList<Appointment> existing,
        DateTime fromUtc,
        DateTime toUtc,
        AppointmentType type,
        IAvailabilityRepository availability,
        CancellationToken ct,
        Guid? onlyUserId = null
    )
    {
        var warnings = new List<string>();
        foreach (var attendee in appointment.Attendees)
        {
            if (
                attendee.UserId is not { } userId
                || (onlyUserId is not null && userId != onlyUserId)
                || userId == appointment.OrganizerUserId
                || attendee.Response == AttendeeResponse.Declined
            )
                continue;

            var name = attendee.Snapshot.DisplayName;
            var busy = Busy(existing, appointment, userId, fromUtc, toUtc);
            var blocks = await availability.ListBlocksAsync(appointment.TenantId, userId, fromUtc, toUtc, ct);
            foreach (var (startUtc, endUtc) in slots)
            {
                var result = ConflictDetector.Check(startUtc, endUtc, type, busy, blocks);
                if (!result.HasConflict)
                    continue;

                // Sin citas en conflicto lo que chocó fue un bloqueo de agenda.
                if (result.Conflicting.Count == 0)
                {
                    warnings.Add($"{name} has time off then.");
                    break;
                }

                if (result.Blocks)
                    return Result.Failure<List<string>>(AppointmentErrors.AttendeeConflict(name));

                warnings.Add($"{name} already has an appointment at {startUtc:yyyy-MM-dd HH:mm} UTC.");
                break;
            }
        }

        return Result.Success(warnings);
    }

    /// <summary>
    /// <see cref="AppointmentType.DailyCap"/>: cuántas de ese tipo tiene el organizador en su día local
    /// (la zona de la cita). Las canceladas no cuentan; la cita misma tampoco.
    /// </summary>
    private static async Task<Result> DailyCapAsync(
        Appointment appointment,
        IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)> slots,
        AppointmentType type,
        IAppointmentRepository appointments,
        CancellationToken ct
    )
    {
        if (type.DailyCap is not { } cap || slots.Count == 0)
            return Result.Success();
        if (!IanaTimeZone.TryFindTimeZone(appointment.Timing.TimeZone.Id, out var zone))
            return Result.Success();

        var from = slots.Min(s => s.StartUtc).AddDays(-1);
        var to = slots.Max(s => s.EndUtc).AddDays(1);
        var candidates = await appointments.ListOrganizerTypeForRangeAsync(
            appointment.TenantId,
            appointment.OrganizerUserId,
            type.Id,
            from,
            to,
            ct
        );

        var perDay = new Dictionary<DateOnly, int>();
        foreach (var other in candidates)
        {
            if (
                other.Id == appointment.Id
                || other.Id == appointment.SplitFromSeriesId
                || other.Status == AppointmentStatus.Cancelled
            )
                continue;
            var expanded = OccurrenceExpander.Expand(other, from, to);
            if (expanded.IsFailure)
                continue;
            foreach (var occurrence in expanded.Value)
            {
                var day = LocalDay(occurrence.StartUtc, zone);
                perDay[day] = perDay.GetValueOrDefault(day) + 1;
            }
        }

        foreach (var day in slots.Select(s => LocalDay(s.StartUtc, zone)).Distinct())
        {
            if (perDay.GetValueOrDefault(day) >= cap)
                return Result.Failure(AppointmentErrors.DailyCapReached);
        }

        return Result.Success();
    }

    private static DateOnly LocalDay(DateTime utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));

    /// <summary>
    /// De una serie se comprueban los próximos 90 días, no las 156 ocurrencias de tres años: el choque
    /// que importa es el de las próximas semanas.
    /// </summary>
    private static (DateTime From, DateTime To) Window(Appointment appointment)
    {
        if (appointment.IsRecurring)
        {
            var from = DateTime.UtcNow;
            return (from, from.AddDays(ConflictDetector.SeriesLookaheadDays));
        }

        var start = appointment.Timing.StartUtc ?? DateTime.UtcNow;
        var end = appointment.Timing.EndUtc ?? start.AddHours(1);
        return (start, end);
    }

    /// <summary>
    /// Lo que ocupa a una persona: lo que organiza y a lo que fue invitada sin rechazarlo. Antes solo lo
    /// que organizaba, y una cita ajena a la que iba no contaba.
    /// </summary>
    private static List<Occurrence> Busy(
        IReadOnlyList<Appointment> existing,
        Appointment self,
        Guid userId,
        DateTime fromUtc,
        DateTime toUtc
    )
    {
        var busy = new List<Occurrence>();
        foreach (var other in existing)
        {
            // La serie de la que sale una partida es la misma cita: tampoco choca con ella.
            if (
                other.Id == self.Id
                || other.Id == self.SplitFromSeriesId
                || other.Status == AppointmentStatus.Cancelled
            )
                continue;

            if (other.OrganizerUserId != userId && !IsGoing(other, userId))
                continue;

            var expanded = OccurrenceExpander.Expand(other, fromUtc, toUtc);
            if (expanded.IsSuccess)
                busy.AddRange(expanded.Value);
        }

        return busy;
    }

    private static bool IsGoing(Appointment appointment, Guid userId)
    {
        foreach (var attendee in appointment.Attendees)
        {
            if (attendee.UserId == userId && attendee.Response != AttendeeResponse.Declined)
                return true;
        }

        return false;
    }
}
