using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.Commands;

public sealed record ScheduleAppointmentCommand(
    Guid TenantId,
    Guid OrganizerUserId,
    string? Title,
    string? Description,
    string? Location,
    Guid AppointmentTypeId,
    string? TimeZoneId,
    DateTime? StartUtc,
    DateTime? EndUtc,
    DateOnly? SeriesStartDate,
    TimeOnly? LocalStartTime,
    TimeSpan? Duration,
    string? RecurrenceRule,
    Guid? CustomerId,
    int? TaxYear,
    bool IsVirtual
);

public static class ScheduleAppointmentHandler
{
    public static async Task<Result<AppointmentWithWarnings>> Handle(
        ScheduleAppointmentCommand command,
        IAppointmentRepository appointments,
        IAppointmentTypeRepository types,
        IAvailabilityRepository availability,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ICalendarMetrics metrics,
        CancellationToken ct
    )
    {
        var built = await AppointmentFactory.BuildAsync(command, types, ct);
        if (built.IsFailure)
            return Result.Failure<AppointmentWithWarnings>(built.Error);

        var (appointment, type) = built.Value;

        var conflict = await ConflictProbe.CheckAsync(appointment, type, appointments, availability, ct);
        if (conflict.IsFailure)
        {
            metrics.RecordConflictDetected(blocked: true);
            return Result.Failure<AppointmentWithWarnings>(conflict.Error);
        }

        if (conflict.Value.Count > 0)
            metrics.RecordConflictDetected(blocked: false);

        appointments.Add(appointment);
        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(AppointmentEvents.Scheduled(appointment, correlation.CorrelationId));
        metrics.RecordCreated(appointment.IsRecurring);

        return Result.Success(new AppointmentWithWarnings(AppointmentResponse.From(appointment), conflict.Value));
    }
}
