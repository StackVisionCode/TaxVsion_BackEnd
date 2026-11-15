using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;
using Wolverine;

namespace TaxVision.Calendar.Application.PublicBookings;

/// <summary>
/// El visitante confirma su email: se crea la cita del anfitrión y la reserva queda confirmada. Si el horario
/// se ocupó, lo toma otro anfitrión libre; si no queda ninguno, la solicitud sigue viva para elegir otro.
/// Un manejo desconocido responde como un código equivocado.
/// </summary>
public static class VerifyPublicBookingHandler
{
    public static async Task<Result<PublicBookingConfirmation>> Handle(
        VerifyPublicBookingCommand command,
        IPublicBookingRepository bookings,
        IBookingPageRepository pages,
        PublicBookingScheduler scheduler,
        PublicBookingNotices notices,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        var booking = await PublicBookingLookup.ByHandleAsync(bookings, command.RequestHandle, ct);
        var page = booking is null ? null : await pages.GetByIdAsync(booking.TenantId, booking.BookingPageId, ct);
        if (booking is null || page is null)
            return Result.Failure<PublicBookingConfirmation>(PublicBookingErrors.InvalidCode);

        // Los intentos fallidos y el email probado se guardan aunque la respuesta sea un error.
        var verified = booking.VerifyCode(command.Code, now);
        var token = verified.IsSuccess ? await scheduler.ConfirmAsync(page, booking, now, ct) : null;
        await unitOfWork.SaveChangesAsync(ct);
        if (verified.IsFailure)
            return Result.Failure<PublicBookingConfirmation>(verified.Error);
        if (token!.IsFailure)
            return Result.Failure<PublicBookingConfirmation>(token.Error);

        await bus.PublishAsync(await notices.ConfirmedAsync(page, booking, token.Value, ct));
        var host = page.FindActiveHost(booking.HostUserId);
        return Result.Success(
            new PublicBookingConfirmation(
                booking.Status.ToString(),
                booking.SlotStartUtc,
                booking.DurationMinutes,
                host?.DisplayName ?? string.Empty,
                host?.MeetingUrl ?? string.Empty,
                PublicBookingEvents.IcsUid(booking.Id),
                booking.IcsSequence
            )
        );
    }
}
