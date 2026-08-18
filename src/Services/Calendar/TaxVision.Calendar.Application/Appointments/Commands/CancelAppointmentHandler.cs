using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.Commands;

public sealed record CancelAppointmentCommand(
    Guid TenantId,
    Guid AppointmentId,
    Guid ActingUserId,
    EditScope? Scope,
    DateTime? OriginalStartUtc,
    string? Reason
);

public static class CancelAppointmentHandler
{
    public static async Task<Result> Handle(
        CancelAppointmentCommand command,
        IAppointmentRepository appointments,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
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

        var cancelled = onlyOne
            ? CancelOccurrence(appointment, command)
            : appointment.Cancel(command.ActingUserId, command.Reason, DateTime.UtcNow);

        if (cancelled.IsFailure)
            return cancelled;

        await unitOfWork.SaveChangesAsync(ct);
        await PublishAsync(appointment, command, onlyOne, bus, correlation);
        metrics.RecordCancelled(appointment.IsRecurring);

        return Result.Success();
    }

    private static Result CancelOccurrence(Appointment appointment, CancelAppointmentCommand command) =>
        command.OriginalStartUtc is not { } original
            ? Result.Failure(RecurrenceErrors.NotAnOccurrence)
            : appointment.CancelOccurrence(original, command.ActingUserId, DateTime.UtcNow);

    /// <summary>
    /// Cancelar sin avisar a Reminder deja el aviso vivo: al cliente le llega un recordatorio de una
    /// cita que ya no existe.
    /// </summary>
    private static async Task PublishAsync(
        Appointment appointment,
        CancelAppointmentCommand command,
        bool onlyOneOccurrence,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        var scope = command.Scope?.ToString() ?? nameof(EditScope.EntireSeries);

        // El aviso a los asistentes sale SIEMPRE, tambien al cancelar una sola ocurrencia. Antes solo
        // salia al cancelar la cita entera, asi que a quien se le caia una reunion de una serie no se
        // le decia nada: se presentaba a algo que ya no existia.
        await bus.PublishAsync(
            new AppointmentCancelledIntegrationEvent
            {
                TenantId = appointment.TenantId,
                CorrelationId = correlation.CorrelationId,
                AppointmentId = appointment.Id,
                Scope = scope,
                OriginalStartUtc = command.OriginalStartUtc,
                Reason = command.Reason,
                Recipients = AppointmentEvents.RecipientsOf(appointment),
            }
        );

        // Y ademas el hecho preciso, que dice cual de las N ocurrencias se cayo.
        if (onlyOneOccurrence)
        {
            await bus.PublishAsync(
                new OccurrenceCancelledIntegrationEvent
                {
                    TenantId = appointment.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    AppointmentId = appointment.Id,
                    OriginalStartUtc = command.OriginalStartUtc!.Value,
                }
            );
        }

        await bus.PublishAsync(
            new ReminderTargetClosedIntegrationEvent
            {
                TenantId = appointment.TenantId,
                CorrelationId = correlation.CorrelationId,
                Category = "Calendar",
                TargetId = OccurrenceTargetId.For(
                    appointment.Id,
                    command.OriginalStartUtc ?? appointment.Timing.StartUtc ?? DateTime.UtcNow
                ),
                Reason = command.Reason,
            }
        );
    }
}
