using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;

namespace TaxVision.Calendar.Application.Appointments.Commands;

public sealed record CancelAppointmentCommand(
    Guid TenantId,
    Guid AppointmentId,
    Guid ActingUserId,
    EditScope? Scope,
    DateTime? OriginalStartUtc,
    string? Reason,
    bool CanManageAll
);

public static class CancelAppointmentHandler
{
    public static async Task<Result> Handle(
        CancelAppointmentCommand command,
        IAppointmentRepository appointments,
        IUnitOfWork unitOfWork,
        ICalendarMetrics metrics,
        CancellationToken ct
    )
    {
        var found = await appointments.GetByIdAsync(command.TenantId, command.AppointmentId, ct);
        if (found.IsFailure)
            return found;

        var appointment = found.Value;
        var onlyOne = appointment.IsRecurring && command.Scope == EditScope.ThisOccurrence;

        if (appointment.IsRecurring && command.Scope is null)
            return Result.Failure(AppointmentErrors.ScopeRequired);

        // Los objetivos se calculan antes de cancelar: después la cita ya no los produce.
        var now = DateTime.UtcNow;
        var targets = ReminderTargetsToClose(appointment, command, onlyOne, now);

        var cancelled = onlyOne
            ? CancelOccurrence(appointment, command, now)
            : appointment.Cancel(
                command.ActingUserId,
                command.CanManageAll,
                command.Reason,
                now,
                command.Scope,
                command.OriginalStartUtc
            );

        if (cancelled.IsFailure)
            return cancelled;

        // Cancelar sin avisar a Reminder deja el aviso vivo: al cliente le llega un recordatorio de una
        // cita que ya no existe.
        appointment.CloseReminderTargets(targets, command.Reason, now);

        await unitOfWork.SaveChangesAsync(ct);
        metrics.RecordCancelled(appointment.IsRecurring);

        return Result.Success();
    }

    private static Result CancelOccurrence(
        Appointment appointment,
        CancelAppointmentCommand command,
        DateTime nowUtc
    ) =>
        command.OriginalStartUtc is not { } original
            ? Result.Failure(RecurrenceErrors.NotAnOccurrence)
            : appointment.CancelOccurrence(
                original,
                command.ActingUserId,
                command.CanManageAll,
                nowUtc,
                command.Reason
            );

    /// <summary>Sin aviso pedido no hay nada que cerrar en Reminder.</summary>
    private static IReadOnlyList<Guid> ReminderTargetsToClose(
        Appointment appointment,
        CancelAppointmentCommand command,
        bool onlyOneOccurrence,
        DateTime nowUtc
    )
    {
        if (appointment.ReminderLeadMinutes is null)
            return [];

        if (onlyOneOccurrence)
            return command.OriginalStartUtc is { } original ? [OccurrenceTargetId.For(appointment.Id, original)] : [];

        return [.. AppointmentReminderTargets.Snapshot(appointment, nowUtc)];
    }
}
