using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Application.PublicBookings;

/// <summary>Arma las partes de los eventos de integración de la reserva pública.</summary>
internal static class PublicBookingEvents
{
    /// <summary>UID estable del evento de calendario: el mismo en el correo, el .ics y el landing.</summary>
    public static string IcsUid(Guid bookingId) => $"public-booking-{bookingId:N}";

    public static string LanguageCode(BookingLanguage language) => language == BookingLanguage.Es ? "es" : "en";

    public static PublicBookingVisitor Visitor(PublicBooking booking) =>
        new(
            booking.Name,
            booking.Email ?? string.Empty,
            booking.Company,
            booking.OfficeSize?.ToString(),
            booking.Phone,
            booking.Interest,
            booking.VisitorTimeZone
        );

    /// <summary>
    /// El anfitrión de la página. Si ya no está en ella (lo quitaron después), sale sin enlace: el correo lo dice
    /// y el equipo reasigna.
    /// </summary>
    /// <summary>
    /// P23 (TZ02-H15): la zona del anfitrión de la página; sin ella, la de su primera regla activa (la más vieja, así
    /// la elección no cambia entre lecturas); sin disponibilidad, la del visitante.
    /// </summary>
    public static async Task<string> HostTimeZoneAsync(
        BookingPage page,
        Guid hostUserId,
        string visitorTimeZone,
        IAvailabilityRepository availability,
        CancellationToken ct
    )
    {
        foreach (var candidate in page.Hosts)
        {
            if (candidate.UserId == hostUserId && candidate.TimeZoneId is { } explicitZone)
                return explicitZone;
        }

        var rules = await availability.ListRulesAsync(page.TenantId, hostUserId, ct);
        var first = rules
            .Where(rule => rule.IsActive)
            .OrderBy(rule => rule.CreatedAtUtc)
            .ThenBy(rule => rule.Id)
            .FirstOrDefault();
        return first?.TimeZone.Id ?? visitorTimeZone;
    }

    public static async Task<PublicBookingHost> HostAsync(
        BookingPage page,
        Guid hostUserId,
        string visitorTimeZone,
        IAvailabilityRepository availability,
        CancellationToken ct
    )
    {
        BookingPageHost? host = null;
        foreach (var candidate in page.Hosts)
        {
            if (candidate.UserId == hostUserId)
                host = candidate;
        }

        var timeZone = await HostTimeZoneAsync(page, hostUserId, visitorTimeZone, availability, ct);

        return new PublicBookingHost(
            hostUserId,
            host?.DisplayName ?? string.Empty,
            host?.NotificationEmail ?? string.Empty,
            host?.MeetingUrl ?? string.Empty,
            timeZone
        );
    }

    public static PublicBookingHeardAbout HeardAbout(BookingPage page, PublicBooking booking)
    {
        BookingPageHeardAboutOption? option = null;
        foreach (var candidate in page.HeardAboutOptions)
        {
            if (candidate.Code == booking.HeardAboutCode)
                option = candidate;
        }
        return new PublicBookingHeardAbout(
            booking.HeardAboutCode,
            option?.LabelEn ?? booking.HeardAboutCode,
            option?.LabelEs,
            booking.HeardAboutDetail
        );
    }

    public static PublicBookingAttribution Attribution(PublicBooking booking) =>
        new(
            booking.EntrySource.ToString(),
            booking.UtmSource,
            booking.UtmMedium,
            booking.UtmCampaign,
            booking.ReferrerDomain
        );

    public static ProtectedPayload ProtectToken(
        IProtectedPayloadCodec codec,
        Guid tenantId,
        Guid bookingId,
        int version,
        string token
    ) =>
        codec.Protect(
            PublicBookingPayloadSchemas.ManagementToken,
            PublicBookingPayloadSchemas.ManagementTokenBinding(tenantId, bookingId, version),
            new PublicBookingSecretPayload(token)
        );
}
