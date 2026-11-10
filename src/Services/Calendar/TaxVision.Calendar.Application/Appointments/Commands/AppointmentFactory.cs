using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.Types;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Application.Appointments.Commands;

/// <summary>
/// Arma la cita a partir del comando. Vive aparte del handler porque son tres validaciones
/// encadenadas y el handler tiene un presupuesto de treinta líneas.
/// </summary>
internal static class AppointmentFactory
{
    public static async Task<Result<(Appointment Appointment, AppointmentType Type)>> BuildAsync(
        ScheduleAppointmentCommand command,
        IAppointmentTypeRepository types,
        CancellationToken ct
    )
    {
        var type = await types.GetByIdAsync(command.TenantId, command.AppointmentTypeId, ct);
        if (type.IsFailure)
            return Result.Failure<(Appointment, AppointmentType)>(type.Error);

        var title = AppointmentTitle.Create(command.Title);
        if (title.IsFailure)
            return Result.Failure<(Appointment, AppointmentType)>(title.Error);

        Location? location = null;
        if (!string.IsNullOrWhiteSpace(command.Location))
        {
            var parsed = Location.Create(command.Location);
            if (parsed.IsFailure)
                return Result.Failure<(Appointment, AppointmentType)>(parsed.Error);

            location = parsed.Value;
        }

        var timing = BuildTiming(command, type.Value.DefaultDuration);
        if (timing.IsFailure)
            return Result.Failure<(Appointment, AppointmentType)>(timing.Error);

        if (
            FirstStart(timing.Value, command) is { } firstStart
            && SchedulingWindow.StartsInThePast(firstStart, DateTime.UtcNow)
        )
            return Result.Failure<(Appointment, AppointmentType)>(TimingErrors.StartsInThePast);

        var attendees = BuildAttendees(command);
        if (attendees.IsFailure)
            return Result.Failure<(Appointment, AppointmentType)>(attendees.Error);

        var appointment = Appointment.Schedule(
            command.TenantId,
            title.Value,
            timing.Value,
            command.AppointmentTypeId,
            command.OrganizerUserId,
            DateTime.UtcNow,
            command.Description,
            location,
            command.CustomerId,
            command.TaxYear,
            command.IsVirtual || type.Value.IsVirtual,
            attendees.Value,
            command.BookedByCustomerName,
            command.AwaitingApproval
        );

        if (appointment.IsFailure)
            return Result.Failure<(Appointment, AppointmentType)>(appointment.Error);

        var reminder = appointment.Value.RequestReminder(command.ReminderLeadMinutes);
        if (reminder.IsFailure)
            return Result.Failure<(Appointment, AppointmentType)>(reminder.Error);

        return string.IsNullOrWhiteSpace(command.RecurrenceRule)
            ? Result.Success((appointment.Value, type.Value))
            : MakeRecurring(appointment.Value, timing.Value, command, type.Value);
    }

    /// <summary>
    /// El organizador primero (aceptado, si se conoce su correo) y después los elegidos. Un asistente
    /// inválido rechaza el alta entera: crearla sin él lo dejaría fuera sin aviso.
    /// </summary>
    private static Result<IReadOnlyList<InitialAttendee>> BuildAttendees(ScheduleAppointmentCommand command)
    {
        var list = new List<InitialAttendee>();
        if (command.Organizer is { Email: { Length: > 0 } email } organizer)
        {
            var name = string.IsNullOrWhiteSpace(organizer.DisplayName) ? email : organizer.DisplayName;
            var snapshot = AttendeeSnapshot.Create(name, email);
            if (snapshot.IsSuccess)
                list.Add(
                    new InitialAttendee(
                        AttendeeKind.InternalUser,
                        command.OrganizerUserId,
                        null,
                        snapshot.Value,
                        true,
                        Accepted: true
                    )
                );
        }

        foreach (var input in command.Attendees)
        {
            var snapshot = AttendeeSnapshot.Create(input.DisplayName, input.Email);
            if (snapshot.IsFailure)
                return Result.Failure<IReadOnlyList<InitialAttendee>>(snapshot.Error);
            var isOrganizer = input.UserId == command.OrganizerUserId;
            list.Add(
                new InitialAttendee(
                    input.Kind,
                    input.UserId,
                    input.CustomerId,
                    snapshot.Value,
                    input.IsRequired,
                    Accepted: isOrganizer || input.Accepted
                )
            );
        }

        return Result.Success<IReadOnlyList<InitialAttendee>>(list);
    }

    /// <summary>
    /// El primer inicio: el de la cita, o el de la serie en su día y hora locales (la regla puede
    /// saltarse ese día, pero empezar la serie antes de hoy ya es empezarla en el pasado).
    /// </summary>
    private static DateTime? FirstStart(EventTiming timing, ScheduleAppointmentCommand command)
    {
        if (timing.StartUtc is { } start)
            return start;
        if (command.SeriesStartDate is not { } date || command.LocalStartTime is not { } time)
            return null;
        if (!IanaTimeZone.TryFindTimeZone(timing.TimeZone.Id, out var zone))
            return null;
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        return zone.IsInvalidTime(local) ? null : TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    /// <summary>Sin fin (o sin duración en una serie) se usa la duración del tipo.</summary>
    private static Result<EventTiming> BuildTiming(ScheduleAppointmentCommand command, TimeSpan typeDuration)
    {
        if (!string.IsNullOrWhiteSpace(command.RecurrenceRule))
        {
            if (command.SeriesStartDate is not { } start || command.LocalStartTime is not { } time)
                return Result.Failure<EventTiming>(TimingErrors.RecurringMustBeLocal);

            var duration = command.Duration ?? typeDuration;
            return EventTiming.RecurringOf(start, time, duration, command.TimeZoneId);
        }

        if (command.StartUtc is not { } startUtc)
            return Result.Failure<EventTiming>(TimingErrors.EndBeforeStart);

        return EventTiming.PointInTimeOf(startUtc, command.EndUtc ?? startUtc + typeDuration, command.TimeZoneId);
    }

    private static Result<(Appointment, AppointmentType)> MakeRecurring(
        Appointment appointment,
        EventTiming timing,
        ScheduleAppointmentCommand command,
        AppointmentType type
    )
    {
        var rule = RecurrenceRule.Create(command.RecurrenceRule);
        if (rule.IsFailure)
            return Result.Failure<(Appointment, AppointmentType)>(rule.Error);

        var applied = appointment.MakeRecurring(rule.Value, timing, command.OrganizerUserId, canManageAll: false);
        return applied.IsFailure
            ? Result.Failure<(Appointment, AppointmentType)>(applied.Error)
            : Result.Success((appointment, type));
    }
}
