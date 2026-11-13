using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings.Events;
using Wolverine;

namespace TaxVision.Calendar.Application.PublicBookings.DomainEvents;

/// <summary>Demo cancelada: correo con el .ics de cancelación a las dos partes.</summary>
public static class PublicBookingCancelledDomainEventHandler
{
    public static async Task<PublicBookingCancelledIntegrationEvent> Handle(
        PublicBookingCancelledDomainEvent domainEvent,
        IPublicBookingRepository bookings,
        IBookingPageRepository pages,
        IAvailabilityRepository availability,
        Envelope envelope,
        CancellationToken ct
    )
    {
        // Nacen en la misma transacción que el evento: si faltan es un error, y va a reintento, no se descarta.
        var booking =
            await bookings.GetByIdAsync(domainEvent.TenantId, domainEvent.BookingId, ct)
            ?? throw new InvalidOperationException($"Public booking {domainEvent.BookingId} was not found.");
        var page =
            await pages.GetByIdAsync(domainEvent.TenantId, booking.BookingPageId, ct)
            ?? throw new InvalidOperationException($"Booking page {booking.BookingPageId} was not found.");

        return new PublicBookingCancelledIntegrationEvent
        {
            TenantId = domainEvent.TenantId,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
            BookingId = booking.Id,
            PageTitle = page.Title.For(booking.Language),
            StartUtc = domainEvent.SlotStartUtc,
            DurationMinutes = booking.DurationMinutes,
            Language = PublicBookingEvents.LanguageCode(booking.Language),
            Visitor = PublicBookingEvents.Visitor(booking),
            Host = await PublicBookingEvents.HostAsync(
                page,
                domainEvent.HostUserId,
                booking.VisitorTimeZone,
                availability,
                ct
            ),
            CancelledBy = domainEvent.CancelledBy.ToString(),
            Reason = domainEvent.Reason,
            IcsUid = PublicBookingEvents.IcsUid(booking.Id),
            IcsSequence = domainEvent.IcsSequence,
        };
    }
}
