using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Api.Requests;

// Lo que manda el equipo de la plataforma (futuro admin panel o Postman). El tenant y el usuario salen del token.

public sealed record BookingPageRequest(
    string? Slug,
    string? TitleEn,
    string? TitleEs,
    string? DescriptionEn,
    string? DescriptionEs,
    Guid AppointmentTypeId
);

public sealed record BookingPageRulesRequest(
    int MinNoticeMinutes,
    int MaxDaysAhead,
    int BufferMinutes,
    int SlotStepMinutes,
    bool RequireEmailVerification,
    int? ReminderHoursBefore,
    IReadOnlyList<int>? IcsAlarmMinutes
);

public sealed record BookingPageFieldsRequest(
    BookingFieldMode PhoneField,
    BookingFieldMode OfficeSizeField,
    BookingFieldMode InterestField
);

public sealed record BookingPageConsentRequest(
    string? PrivacyNoticeEn,
    string? PrivacyNoticeEs,
    string? MarketingConsentEn,
    string? MarketingConsentEs,
    string? ConsentVersion
);

public sealed record BookingPageHostsRequest(IReadOnlyList<BookingPageHostInput>? Hosts);

public sealed record BookingPageHeardAboutOptionsRequest(IReadOnlyList<HeardAboutOptionInput>? Options);

public sealed record StaffCancelPublicBookingRequest(string? Reason);

public sealed record StaffReschedulePublicBookingRequest(DateTime SlotStartUtc, string? Reason);

public sealed record PublicBookingOutcomeRequest(PublicBookingOutcome Outcome);
