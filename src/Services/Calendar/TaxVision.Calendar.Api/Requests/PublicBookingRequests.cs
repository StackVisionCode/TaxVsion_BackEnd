using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Api.Requests;

// Lo que manda el landing. Ninguna petición pública lleva tenant, usuario ni anfitrión.

public sealed record PublicBookingCreateRequest(
    DateTime SlotStartUtc,
    string? Name,
    string? Email,
    string? Company,
    OfficeSize? OfficeSize,
    string? Phone,
    string? Interest,
    string? HeardAboutCode,
    string? HeardAboutDetail,
    BookingEntrySource EntrySource,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    string? ReferrerDomain,
    BookingLanguage Language,
    string? TimeZone,
    bool MarketingOptIn,
    string? ConsentVersion,
    string? BotToken
);

public sealed record PublicBookingVerifyRequest(string? RequestHandle, string? Code);

public sealed record PublicBookingChooseSlotRequest(string? RequestHandle, DateTime SlotStartUtc);

public sealed record PublicBookingResendRequest(string? RequestHandle);

public sealed record PublicBookingManageRequest(string? Token);

public sealed record PublicBookingRescheduleRequest(string? Token, DateTime SlotStartUtc);

public sealed record PublicBookingCancelRequest(string? Token, string? Reason);
