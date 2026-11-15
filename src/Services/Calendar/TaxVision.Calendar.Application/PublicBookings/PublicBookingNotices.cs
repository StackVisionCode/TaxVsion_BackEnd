using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Application.PublicBookings;

/// <summary>
/// Arma los eventos que llevan un secreto (código o enlace de gestión) ya cifrado. Los usa el handler del
/// comando que tiene el secreto en mano, después de guardar: así el secreto en claro nunca toca la base,
/// ni en un evento de dominio ni en el outbox.
/// </summary>
public sealed class PublicBookingNotices(
    IAvailabilityRepository availability,
    IProtectedPayloadCodec codec,
    ICorrelationContext correlation
)
{
    public PublicBookingCodeRequestedIntegrationEvent CodeRequested(
        BookingPage page,
        PublicBooking booking,
        string code
    ) =>
        new()
        {
            TenantId = booking.TenantId,
            CorrelationId = correlation.CorrelationId,
            BookingId = booking.Id,
            PageTitle = page.Title.For(booking.Language),
            Language = PublicBookingEvents.LanguageCode(booking.Language),
            VisitorName = booking.Name,
            VisitorEmail = booking.Email ?? string.Empty,
            ExpiresAtUtc = booking.HoldExpiresAtUtc,
            Send = booking.CodeSends,
            ProtectedCode = codec.Protect(
                PublicBookingPayloadSchemas.VerificationCode,
                PublicBookingPayloadSchemas.CodeBinding(booking.TenantId, booking.Id, booking.CodeSends),
                new PublicBookingSecretPayload(code)
            ),
        };

    public async Task<PublicBookingConfirmedIntegrationEvent> ConfirmedAsync(
        BookingPage page,
        PublicBooking booking,
        string managementToken,
        CancellationToken ct
    ) =>
        new()
        {
            TenantId = booking.TenantId,
            CorrelationId = correlation.CorrelationId,
            BookingId = booking.Id,
            AppointmentId = booking.AppointmentId ?? Guid.Empty,
            PageTitle = page.Title.For(booking.Language),
            StartUtc = booking.SlotStartUtc,
            DurationMinutes = booking.DurationMinutes,
            Language = PublicBookingEvents.LanguageCode(booking.Language),
            Visitor = PublicBookingEvents.Visitor(booking),
            Host = await HostAsync(page, booking, ct),
            HeardAbout = PublicBookingEvents.HeardAbout(page, booking),
            Attribution = PublicBookingEvents.Attribution(booking),
            MarketingOptIn = booking.MarketingOptIn,
            IcsUid = PublicBookingEvents.IcsUid(booking.Id),
            IcsSequence = booking.IcsSequence,
            IcsAlarmMinutes = page.IcsAlarmMinutes,
            ManagementTokenVersion = booking.ManagementTokenVersion,
            ProtectedManagementToken = Protect(booking, managementToken),
        };

    public async Task<PublicBookingRescheduledIntegrationEvent> RescheduledAsync(
        BookingPage page,
        PublicBooking booking,
        DateTime previousStartUtc,
        PublicBookingActor changedBy,
        string? reason,
        string managementToken,
        CancellationToken ct
    ) =>
        new()
        {
            TenantId = booking.TenantId,
            CorrelationId = correlation.CorrelationId,
            BookingId = booking.Id,
            AppointmentId = booking.AppointmentId ?? Guid.Empty,
            PageTitle = page.Title.For(booking.Language),
            PreviousStartUtc = previousStartUtc,
            StartUtc = booking.SlotStartUtc,
            DurationMinutes = booking.DurationMinutes,
            Language = PublicBookingEvents.LanguageCode(booking.Language),
            Visitor = PublicBookingEvents.Visitor(booking),
            Host = await HostAsync(page, booking, ct),
            ChangedBy = changedBy.ToString(),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            IcsUid = PublicBookingEvents.IcsUid(booking.Id),
            IcsSequence = booking.IcsSequence,
            IcsAlarmMinutes = page.IcsAlarmMinutes,
            ManagementTokenVersion = booking.ManagementTokenVersion,
            ProtectedManagementToken = Protect(booking, managementToken),
        };

    public async Task<PublicBookingReminderDueIntegrationEvent> ReminderAsync(
        BookingPage page,
        PublicBooking booking,
        int hoursBefore,
        string managementToken,
        CancellationToken ct
    ) =>
        new()
        {
            TenantId = booking.TenantId,
            CorrelationId = correlation.CorrelationId,
            BookingId = booking.Id,
            PageTitle = page.Title.For(booking.Language),
            StartUtc = booking.SlotStartUtc,
            DurationMinutes = booking.DurationMinutes,
            Language = PublicBookingEvents.LanguageCode(booking.Language),
            Visitor = PublicBookingEvents.Visitor(booking),
            Host = await HostAsync(page, booking, ct),
            HoursBefore = hoursBefore,
            ManagementTokenVersion = booking.ManagementTokenVersion,
            ProtectedManagementToken = Protect(booking, managementToken),
        };

    public async Task<PublicBookingAlreadyBookedIntegrationEvent> AlreadyBookedAsync(
        BookingPage page,
        PublicBooking booking,
        string managementToken,
        CancellationToken ct
    ) =>
        new()
        {
            TenantId = booking.TenantId,
            CorrelationId = correlation.CorrelationId,
            BookingId = booking.Id,
            PageTitle = page.Title.For(booking.Language),
            StartUtc = booking.SlotStartUtc,
            DurationMinutes = booking.DurationMinutes,
            Language = PublicBookingEvents.LanguageCode(booking.Language),
            Visitor = PublicBookingEvents.Visitor(booking),
            Host = await HostAsync(page, booking, ct),
            ManagementTokenVersion = booking.ManagementTokenVersion,
            ProtectedManagementToken = Protect(booking, managementToken),
        };

    private Task<PublicBookingHost> HostAsync(BookingPage page, PublicBooking booking, CancellationToken ct) =>
        PublicBookingEvents.HostAsync(page, booking.HostUserId, booking.VisitorTimeZone, availability, ct);

    private ProtectedPayload Protect(PublicBooking booking, string managementToken) =>
        PublicBookingEvents.ProtectToken(
            codec,
            booking.TenantId,
            booking.Id,
            booking.ManagementTokenVersion,
            managementToken
        );
}
