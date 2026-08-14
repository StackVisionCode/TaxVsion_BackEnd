using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.Commands;

public sealed record RespondToAppointmentCommand(
    Guid TenantId,
    Guid AppointmentId,
    Guid RespondingUserId,
    AttendeeResponse Response
);

public static class RespondToAppointmentHandler
{
    public static async Task<Result> Handle(
        RespondToAppointmentCommand command,
        IAppointmentRepository appointments,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var found = await appointments.GetByIdAsync(command.TenantId, command.AppointmentId, ct);
        if (found.IsFailure)
            return found;

        var appointment = found.Value;
        var responded = appointment.RespondAsAttendee(
            command.Response,
            command.RespondingUserId,
            null,
            null,
            DateTime.UtcNow
        );

        if (responded.IsFailure)
            return responded;

        await unitOfWork.SaveChangesAsync(ct);

        // Va al organizador: es quien decide si mueve la cita porque alguien no puede.
        await bus.PublishAsync(
            new AppointmentAttendeeRespondedIntegrationEvent
            {
                TenantId = appointment.TenantId,
                CorrelationId = correlation.CorrelationId,
                AppointmentId = appointment.Id,
                OrganizerUserId = appointment.OrganizerUserId,
                UserId = command.RespondingUserId,
                Response = command.Response.ToString(),
            }
        );

        return Result.Success();
    }
}
