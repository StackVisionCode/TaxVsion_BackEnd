using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Application.PublicBookings;

/// <summary>El visitante cancela con el enlace de gestión: la reserva y la cita del anfitrión, juntas.</summary>
public static class CancelPublicBookingHandler
{
    public static async Task<Result> Handle(
        CancelPublicBookingCommand command,
        IPublicBookingRepository bookings,
        IAppointmentRepository appointments,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        var booking = await PublicBookingLookup.ByTokenAsync(bookings, command.Token, ct);
        if (booking?.AppointmentId is not { } appointmentId)
            return Result.Failure(PublicBookingErrors.NotFound);
        var appointment = await appointments.GetByIdAsync(booking.TenantId, appointmentId, ct);
        if (appointment.IsFailure)
            return Result.Failure(PublicBookingErrors.NotFound);

        // La cita se revisa antes de tocar la reserva: Wolverine guarda aunque el handler devuelva un error.
        var canChange = appointment.Value.CanChangeFromPublicPage();
        if (canChange.IsFailure)
            return canChange;
        var cancelled = booking.CancelByVisitor(now, command.Reason);
        if (cancelled.IsFailure)
            return cancelled;
        appointment.Value.CancelByExternalRequester(command.Reason, now);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
