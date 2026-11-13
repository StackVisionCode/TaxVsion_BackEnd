using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Domain.PublicBookings;

/// <summary>
/// Lo que escribe el visitante, ya validado como value objects. <c>RequesterKey</c> es el HMAC del email con una
/// clave del servidor: identifica al visitante en los índices sin que un diccionario lo revierta.
/// </summary>
public sealed record PublicBookingRequestData(
    ProspectName Name,
    WorkEmail Email,
    string RequesterKey,
    CompanyName Company,
    OfficeSize? OfficeSize,
    ProspectPhone? Phone,
    DemoInterest? Interest,
    HeardAboutAnswer HeardAbout,
    BookingAttribution Attribution,
    BookingLanguage Language,
    CalendarTimeZone VisitorTimeZone,
    bool MarketingOptIn,
    string? ConsentTextVersion
);
