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

/// <summary>El cliente cancela su cita en línea, solo antes del plazo de la oficina.</summary>
public static class CancelAsCustomerHandler
{
    public static async Task<Result> Handle(
        CancelAsCustomerCommand command,
        IPortalCalendarStore store,
        IBookingPolicyRepository policies,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var appointment = await store.FindForCustomerAsync(
            command.TenantId,
            command.CustomerId,
            command.AppointmentId,
            ct
        );
        if (appointment is null)
            return Result.Failure(AppointmentErrors.NotFound);

        var check = PortalChange.Allowed(appointment, await policies.GetAsync(command.TenantId, ct));
        if (check.IsFailure)
            return check;

        var reason = string.IsNullOrWhiteSpace(command.Reason) ? "Cancelled by the client" : command.Reason.Trim();
        var cancelled = appointment.CancelByCustomer(command.CustomerId, command.PortalUserId, reason, DateTime.UtcNow);
        if (cancelled.IsFailure)
            return cancelled;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>
/// La política de la oficina para cambios en línea (otro agregado): reserva activa y fuera del plazo.
/// Quién puede cambiar la cita lo decide la propia cita (<c>CancelByCustomer</c>/<c>RescheduleByCustomer</c>).
/// </summary>
internal static class PortalChange
{
    public static Result Allowed(Appointment appointment, BookingPolicy? policy)
    {
        // Retirar una solicitud pendiente no tiene plazo: todavía no es una cita de nadie.
        if (appointment.Status == AppointmentStatus.Requested)
            return Result.Success();
        if (policy is not { Enabled: true })
            return Result.Failure(BookingErrors.NotAvailable);

        var start = appointment.Timing.StartUtc ?? DateTime.MinValue;
        return PortalAppointmentMapper.CanChange(appointment, policy, start, DateTime.UtcNow)
            ? Result.Success()
            : Result.Failure(BookingErrors.ChangeTooLate);
    }
}
