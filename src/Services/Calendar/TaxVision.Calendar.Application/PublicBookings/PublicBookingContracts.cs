using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Application.PublicBookings;

// Consultas y comandos públicos: nunca llevan tenant, usuario ni anfitrión.

public sealed record GetPublicBookingPageQuery(string Slug, string? Language);

public sealed record GetPublicBookingSlotsQuery(string Slug, DateTime FromUtc, DateTime ToUtc);

/// <summary>Lo que escribe el visitante, tal cual llega del formulario.</summary>
public sealed record PublicBookingForm(
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
    string? ConsentVersion
);

/// <summary>
/// <c>PassedBotCheck</c> llega resuelto: Turnstile se verifica en el controlador, antes de abrir la transacción,
/// para no retener una conexión de base mientras se espera a Cloudflare.
/// </summary>
public sealed record RequestPublicBookingCommand(
    string Slug,
    string? IdempotencyKey,
    bool PassedBotCheck,
    PublicBookingForm Form
);

public sealed record VerifyPublicBookingCommand(string? RequestHandle, string? Code);

public sealed record ChoosePublicBookingSlotCommand(string? RequestHandle, DateTime SlotStartUtc);

public sealed record ResendPublicBookingCodeCommand(string? RequestHandle);

public sealed record ViewPublicBookingQuery(string? Token);

public sealed record ReschedulePublicBookingCommand(string? Token, DateTime SlotStartUtc);

public sealed record CancelPublicBookingCommand(string? Token, string? Reason);

// ---------- Respuestas ----------

public sealed record PublicHeardAboutOption(string Code, string Label, bool AllowsDetail);

/// <summary>Quién puede dar la demo: solo el nombre. El email de avisos y el enlace de la reunión no salen.</summary>
public sealed record PublicBookingHostSummary(string DisplayName);

public sealed record PublicBookingPageResponse(
    string Slug,
    string Title,
    string Description,
    int DurationMinutes,
    int MaxDaysAhead,
    bool RequiresVerification,
    bool BookingAvailable,
    string? TurnstileSiteKey,
    string PhoneField,
    string OfficeSizeField,
    string InterestField,
    IReadOnlyList<PublicHeardAboutOption> HeardAboutOptions,
    string PrivacyNotice,
    string MarketingConsent,
    string ConsentVersion,
    IReadOnlyList<PublicBookingHostSummary> Hosts
);

public sealed record PublicBookingSlot(DateTime StartUtc);

public sealed record PublicBookingSlotsResponse(IReadOnlyList<PublicBookingSlot> Slots);

/// <summary>La solicitud quedó (o así lo parece: a un bot se le responde igual, sin efecto).</summary>
public sealed record PublicBookingRequestAccepted(string RequestHandle, DateTime HoldExpiresAtUtc);

public sealed record PublicBookingConfirmation(
    string Status,
    DateTime StartUtc,
    int DurationMinutes,
    string HostName,
    string MeetingUrl,
    string IcsUid,
    int IcsSequence
);

public sealed record PublicBookingManageView(
    string Status,
    DateTime StartUtc,
    int DurationMinutes,
    string Title,
    string HostName,
    string MeetingUrl,
    string TimeZone,
    string PageSlug,
    bool CanChange
);

/// <summary>Movida: el enlace anterior dejó de servir y el nuevo vuelve una sola vez al mismo visitante.</summary>
public sealed record PublicBookingRescheduled(PublicBookingManageView Booking, string ManagementToken);
