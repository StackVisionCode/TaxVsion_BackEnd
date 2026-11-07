using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Application.Appointments.Commands;

/// <param name="Scope">
/// Obligatorio sobre una serie y sin valor por defecto: elegirlo en silencio reescribe el pasado o
/// frustra a quien quería mover todo.
/// </param>
/// <param name="ReminderLeadMinutes">
/// Minutos del aviso. Null lo deja como estaba y 0 lo quita. No aplica a «solo esta ocurrencia».
/// </param>
public sealed record RescheduleAppointmentCommand(
    Guid TenantId,
    Guid AppointmentId,
    Guid ActingUserId,
    EditScope? Scope,
    DateTime? OriginalStartUtc,
    DateTime? NewStartUtc,
    DateTime? NewEndUtc,
    DateOnly? SeriesStartDate,
    TimeOnly? LocalStartTime,
    TimeSpan? Duration,
    string? TimeZoneId,
    string? RecurrenceRule,
    bool CanManageAll,
    int? ReminderLeadMinutes = null
);

public static class RescheduleAppointmentHandler
{
    public static async Task<Result<AppointmentResponse>> Handle(
        RescheduleAppointmentCommand command,
        IAppointmentRepository appointments,
        IAppointmentTypeRepository types,
        IAvailabilityRepository availability,
        IOrganizerScheduleLock scheduleLock,
        IUnitOfWork unitOfWork,
        ICalendarMetrics metrics,
        CancellationToken ct
    )
    {
        var found = await appointments.GetByIdAsync(command.TenantId, command.AppointmentId, ct);
        if (found.IsFailure)
            return Result.Failure<AppointmentResponse>(found.Error);

        var appointment = found.Value;
        var type = await types.GetByIdAsync(command.TenantId, appointment.AppointmentTypeId, ct);
        if (type.IsFailure)
            return Result.Failure<AppointmentResponse>(type.Error);

        // Comprobar y guardar en serie por organizador, igual que el alta (D-CAL-2). Se lee el organizador de la cita:
        // quien mueve puede ser otra persona con permiso sobre todas.
        await scheduleLock.AcquireAsync(command.TenantId, appointment.OrganizerUserId, ct);

        var scheduleBefore = appointment.ScheduleStamp;
        var applied = RescheduleApplier.Apply(appointment, command, appointments);
        if (applied.IsFailure)
            return Result.Failure<AppointmentResponse>(applied.Error);

        // Se comprueba después de aplicar para que sus errores vayan primero. Sin cambio de hora no hay nada que comprobar.
        if (applied.Value != appointment || appointment.ScheduleStamp != scheduleBefore)
        {
            var conflict = await RescheduleApplier.CheckConflictsAsync(
                applied.Value,
                command,
                type.Value,
                appointments,
                availability,
                ct
            );
            if (conflict.IsFailure)
            {
                metrics.RecordConflictDetected(blocked: true);
                appointments.DiscardPendingChanges();
                return Result.Failure<AppointmentResponse>(conflict.Error);
            }

            if (conflict.Value.Count > 0)
                metrics.RecordConflictDetected(blocked: false);
        }

        await unitOfWork.SaveChangesAsync(ct);

        metrics.RecordRescheduled(appointment.IsRecurring);

        return Result.Success(AppointmentResponse.From(applied.Value));
    }
}
