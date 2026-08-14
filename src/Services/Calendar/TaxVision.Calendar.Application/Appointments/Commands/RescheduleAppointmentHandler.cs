using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.Commands;

/// <param name="Scope">
/// Obligatorio sobre una serie y sin valor por defecto: elegirlo en silencio reescribe el pasado o
/// frustra a quien quería mover todo.
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
    string? RecurrenceRule
);

public static class RescheduleAppointmentHandler
{
    public static async Task<Result<AppointmentResponse>> Handle(
        RescheduleAppointmentCommand command,
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
            return Result.Failure<AppointmentResponse>(found.Error);

        var appointment = found.Value;
        var applied = await RescheduleApplier.ApplyAsync(appointment, command, appointments, bus, correlation);
        if (applied.IsFailure)
            return Result.Failure<AppointmentResponse>(applied.Error);

        await unitOfWork.SaveChangesAsync(ct);
        metrics.RecordRescheduled(appointment.IsRecurring);

        return Result.Success(AppointmentResponse.From(applied.Value));
    }
}
