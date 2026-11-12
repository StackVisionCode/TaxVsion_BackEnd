using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Booking.Abstractions;
using TaxVision.Calendar.Application.Office.Abstractions;
using TaxVision.Calendar.Application.Portal.Abstractions;
using TaxVision.Calendar.Application.Staff.Abstractions;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Booking;
using TaxVision.Calendar.Domain.Scheduling;

namespace TaxVision.Calendar.Application.Portal;

/// <summary>El cliente responde a su invitación desde el portal; avisa al organizador como en el CRM.</summary>
public static class RespondAsCustomerHandler
{
    public static async Task<Result> Handle(
        RespondAsCustomerCommand command,
        IPortalCalendarStore store,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (command.Response == AttendeeResponse.NeedsAction)
            return Result.Failure(AppointmentErrors.InvalidResponse);

        var appointment = await store.FindForCustomerAsync(
            command.TenantId,
            command.CustomerId,
            command.AppointmentId,
            ct
        );
        if (appointment is null)
            return Result.Failure(AppointmentErrors.NotFound);

        var responded = appointment.RespondAsAttendee(
            command.Response,
            null,
            command.CustomerId,
            null,
            DateTime.UtcNow
        );
        if (responded.IsFailure)
            return responded;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
