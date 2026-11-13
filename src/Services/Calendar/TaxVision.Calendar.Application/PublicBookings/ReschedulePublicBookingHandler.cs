using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;
using Wolverine;

namespace TaxVision.Calendar.Application.PublicBookings;

/// <summary>El visitante mueve su demo con el enlace de gestión. El enlace rota: el nuevo vuelve en la respuesta.</summary>
public static class ReschedulePublicBookingHandler
{
    public static async Task<Result<PublicBookingRescheduled>> Handle(
        ReschedulePublicBookingCommand command,
        IPublicBookingRepository bookings,
        IBookingPageRepository pages,
        IAppointmentRepository appointments,
        PublicBookingScheduler scheduler,
        PublicBookingNotices notices,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        var booking = await PublicBookingLookup.ByTokenAsync(bookings, command.Token, ct);
        var page = booking is null ? null : await pages.GetByIdAsync(booking.TenantId, booking.BookingPageId, ct);
        if (booking?.AppointmentId is not { } appointmentId || page is null)
            return Result.Failure<PublicBookingRescheduled>(PublicBookingErrors.NotFound);
        var appointment = await appointments.GetByIdAsync(booking.TenantId, appointmentId, ct);
        if (appointment.IsFailure)
            return Result.Failure<PublicBookingRescheduled>(PublicBookingErrors.NotFound);

        var previous = booking.SlotStartUtc;
        var token = await scheduler.MoveAsync(page, booking, appointment.Value, command.SlotStartUtc, now, ct);
        if (token.IsFailure)
            return Result.Failure<PublicBookingRescheduled>(token.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            await notices.RescheduledAsync(page, booking, previous, PublicBookingActor.Visitor, null, token.Value, ct)
        );
        return Result.Success(
            new PublicBookingRescheduled(ViewPublicBookingHandler.ViewOf(page, booking, now), token.Value)
        );
    }
}
