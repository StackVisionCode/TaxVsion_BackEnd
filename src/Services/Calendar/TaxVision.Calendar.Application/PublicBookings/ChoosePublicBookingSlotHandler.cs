using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Application.PublicBookings;

/// <summary>
/// Con la solicitud viva y el email ya probado, el visitante elige otro horario sin repetir el código. Un manejo
/// desconocido responde lo mismo que uno real sin verificar.
/// </summary>
public static class ChoosePublicBookingSlotHandler
{
    public static async Task<Result> Handle(
        ChoosePublicBookingSlotCommand command,
        IPublicBookingRepository bookings,
        IBookingPageRepository pages,
        PublicBookingScheduler scheduler,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        var booking = await PublicBookingLookup.ByHandleAsync(bookings, command.RequestHandle, ct);
        if (booking is null)
            return Result.Failure(PublicBookingErrors.NotVerified);
        var allowed = booking.CanChooseSlot(now);
        if (allowed.IsFailure)
            return allowed;
        var page = await pages.GetByIdAsync(booking.TenantId, booking.BookingPageId, ct);
        var type = page is null ? null : await scheduler.TypeOfAsync(page, ct);
        if (page is null || type is null)
            return Result.Failure(PublicBookingErrors.HoldExpired);

        var host = await scheduler.ClaimHostAsync(
            page,
            type,
            command.SlotStartUtc,
            booking.HostUserId,
            booking.Id,
            null,
            now,
            ct
        );
        if (host is null)
            return Result.Failure(PublicBookingErrors.SlotTaken);
        var chosen = booking.ChooseSlot(command.SlotStartUtc, host.Value, now);
        if (chosen.IsFailure)
            return chosen;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
