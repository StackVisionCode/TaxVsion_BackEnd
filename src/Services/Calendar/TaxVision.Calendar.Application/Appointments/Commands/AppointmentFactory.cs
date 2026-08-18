using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
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

        var timing = BuildTiming(command);
        if (timing.IsFailure)
            return Result.Failure<(Appointment, AppointmentType)>(timing.Error);

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
            command.IsVirtual || type.Value.IsVirtual
        );

        if (appointment.IsFailure)
            return Result.Failure<(Appointment, AppointmentType)>(appointment.Error);

        return string.IsNullOrWhiteSpace(command.RecurrenceRule)
            ? Result.Success((appointment.Value, type.Value))
            : MakeRecurring(appointment.Value, timing.Value, command, type.Value);
    }

    private static Result<EventTiming> BuildTiming(ScheduleAppointmentCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.RecurrenceRule))
        {
            if (command.SeriesStartDate is not { } start || command.LocalStartTime is not { } time)
                return Result.Failure<EventTiming>(TimingErrors.RecurringMustBeLocal);

            var duration = command.Duration ?? TimeSpan.FromHours(1);
            return EventTiming.RecurringOf(start, time, duration, command.TimeZoneId);
        }

        if (command.StartUtc is not { } startUtc || command.EndUtc is not { } endUtc)
            return Result.Failure<EventTiming>(TimingErrors.EndBeforeStart);

        return EventTiming.PointInTimeOf(startUtc, endUtc, command.TimeZoneId);
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

        var applied = appointment.MakeRecurring(rule.Value, timing, command.OrganizerUserId);
        return applied.IsFailure
            ? Result.Failure<(Appointment, AppointmentType)>(applied.Error)
            : Result.Success((appointment, type));
    }
}
