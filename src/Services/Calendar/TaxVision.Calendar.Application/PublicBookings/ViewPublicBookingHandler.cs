using BuildingBlocks.Results;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Application.PublicBookings;

/// <summary>Lo que ve el visitante en "Manage your demo". Un enlace inválido, vencido o reemplazado no existe.</summary>
public static class ViewPublicBookingHandler
{
    public static async Task<Result<PublicBookingManageView>> Handle(
        ViewPublicBookingQuery query,
        IPublicBookingRepository bookings,
        IBookingPageRepository pages,
        CancellationToken ct
    )
    {
        var booking = await PublicBookingLookup.ByTokenAsync(bookings, query.Token, ct);
        var page = booking is null ? null : await pages.GetByIdAsync(booking.TenantId, booking.BookingPageId, ct);
        return booking is null || page is null
            ? Result.Failure<PublicBookingManageView>(PublicBookingErrors.NotFound)
            : Result.Success(ViewOf(page, booking, DateTime.UtcNow));
    }

    internal static PublicBookingManageView ViewOf(BookingPage page, PublicBooking booking, DateTime nowUtc)
    {
        var host = page.FindActiveHost(booking.HostUserId);
        return new PublicBookingManageView(
            booking.Status.ToString(),
            booking.SlotStartUtc,
            booking.DurationMinutes,
            page.Title.For(booking.Language),
            host?.DisplayName ?? string.Empty,
            host?.MeetingUrl ?? string.Empty,
            booking.VisitorTimeZone,
            page.Slug.Value,
            booking.Status == PublicBookingStatus.Confirmed && nowUtc < booking.SlotStartUtc
        );
    }
}
