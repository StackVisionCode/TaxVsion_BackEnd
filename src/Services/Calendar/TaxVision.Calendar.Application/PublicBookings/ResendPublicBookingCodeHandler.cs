using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using Wolverine;

namespace TaxVision.Calendar.Application.PublicBookings;

/// <summary>
/// Otro código (hasta tres). Siempre responde "enviado": un manejo desconocido, vencido o sin envíos restantes no
/// se distingue de uno válido; el landing ya invita a revisar el correo o a elegir de nuevo.
/// </summary>
public static class ResendPublicBookingCodeHandler
{
    public static async Task<Result> Handle(
        ResendPublicBookingCodeCommand command,
        IPublicBookingRepository bookings,
        IBookingPageRepository pages,
        PublicBookingNotices notices,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var booking = await PublicBookingLookup.ByHandleAsync(bookings, command.RequestHandle, ct);
        var page = booking is null ? null : await pages.GetByIdAsync(booking.TenantId, booking.BookingPageId, ct);
        if (booking is null || page is null)
            return Result.Success();

        var code = booking.ResendCode(DateTime.UtcNow);
        if (code.IsFailure)
            return Result.Success();
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(notices.CodeRequested(page, booking, code.Value));
        return Result.Success();
    }
}
