using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Application.PublicBookings.Platform;

// Lo que hace el equipo de la plataforma. El tenant y el usuario salen del token, nunca del cuerpo.

public sealed record CreateBookingPageCommand(
    Guid TenantId,
    Guid UserId,
    string? Slug,
    string? TitleEn,
    string? TitleEs,
    string? DescriptionEn,
    string? DescriptionEs,
    Guid AppointmentTypeId
);

public sealed record UpdateBookingPageCommand(
    Guid TenantId,
    Guid UserId,
    Guid PageId,
    string? Slug,
    string? TitleEn,
    string? TitleEs,
    string? DescriptionEn,
    string? DescriptionEs,
    Guid AppointmentTypeId
);

public sealed record ChangeBookingPageRulesCommand(
    Guid TenantId,
    Guid UserId,
    Guid PageId,
    int MinNoticeMinutes,
    int MaxDaysAhead,
    int BufferMinutes,
    int SlotStepMinutes,
    bool RequireEmailVerification,
    int? ReminderHoursBefore,
    IReadOnlyList<int> IcsAlarmMinutes
);

public sealed record ConfigureBookingPageFieldsCommand(
    Guid TenantId,
    Guid UserId,
    Guid PageId,
    BookingFieldMode PhoneField,
    BookingFieldMode OfficeSizeField,
    BookingFieldMode InterestField
);

public sealed record ChangeBookingPageConsentCommand(
    Guid TenantId,
    Guid UserId,
    Guid PageId,
    string? PrivacyNoticeEn,
    string? PrivacyNoticeEs,
    string? MarketingConsentEn,
    string? MarketingConsentEs,
    string? ConsentVersion
);

public sealed record SetBookingPageHostsCommand(
    Guid TenantId,
    Guid UserId,
    Guid PageId,
    IReadOnlyList<BookingPageHostInput> Hosts
);

public sealed record VerifyPlatformHostsQuery(Guid TenantId, IReadOnlyList<Guid> UserIds);

public sealed record SetBookingPageHeardAboutOptionsCommand(
    Guid TenantId,
    Guid UserId,
    Guid PageId,
    IReadOnlyList<HeardAboutOptionInput> Options
);

public sealed record ActivateBookingPageCommand(Guid TenantId, Guid UserId, Guid PageId);

public sealed record DeactivateBookingPageCommand(Guid TenantId, Guid UserId, Guid PageId);

public sealed record ListBookingPagesQuery(Guid TenantId);

public sealed record GetBookingPageQuery(Guid TenantId, Guid PageId);

public sealed record ListPublicBookingsQuery(
    Guid TenantId,
    Guid PageId,
    DateTime FromUtc,
    DateTime ToUtc,
    PublicBookingStatus? Status,
    string? HeardAbout,
    BookingEntrySource? Source,
    int Page = 1,
    int PageSize = 50
);

public sealed record GetPublicBookingSummaryQuery(Guid TenantId, Guid PageId, DateTime FromUtc, DateTime ToUtc);

public sealed record StaffCancelPublicBookingCommand(Guid TenantId, Guid UserId, Guid BookingId, string? Reason);

public sealed record StaffReschedulePublicBookingCommand(
    Guid TenantId,
    Guid UserId,
    Guid BookingId,
    DateTime SlotStartUtc,
    string? Reason
);

public sealed record MarkPublicBookingOutcomeCommand(
    Guid TenantId,
    Guid UserId,
    Guid BookingId,
    PublicBookingOutcome Outcome
);

// ---------- Respuestas ----------

public sealed record BookingPageHostView(
    Guid UserId,
    string DisplayName,
    string NotificationEmail,
    string MeetingUrl,
    int SortOrder,
    bool IsActive
);

public sealed record HeardAboutOptionView(
    string Code,
    string LabelEn,
    string? LabelEs,
    int SortOrder,
    bool IsActive,
    bool AllowsDetail
);

public sealed record BookingPageAdminView(
    Guid Id,
    string Slug,
    string TitleEn,
    string? TitleEs,
    string DescriptionEn,
    string? DescriptionEs,
    Guid AppointmentTypeId,
    bool IsActive,
    int MinNoticeMinutes,
    int MaxDaysAhead,
    int BufferMinutes,
    int SlotStepMinutes,
    bool RequireEmailVerification,
    int? ReminderHoursBefore,
    IReadOnlyList<int> IcsAlarmMinutes,
    string PhoneField,
    string OfficeSizeField,
    string InterestField,
    string? PrivacyNoticeEn,
    string? PrivacyNoticeEs,
    string? MarketingConsentEn,
    string? MarketingConsentEs,
    string ConsentVersion,
    IReadOnlyList<BookingPageHostView> Hosts,
    IReadOnlyList<HeardAboutOptionView> HeardAboutOptions,
    Guid UpdatedByUserId,
    DateTime UpdatedAtUtc
);

public sealed record BookingPageSummary(
    Guid Id,
    string Slug,
    string TitleEn,
    bool IsActive,
    int ActiveHosts,
    DateTime UpdatedAtUtc
);

public sealed record PublicBookingAdminItem(
    Guid Id,
    string Status,
    DateTime SlotStartUtc,
    int DurationMinutes,
    Guid HostUserId,
    string HostName,
    string Name,
    string? Email,
    string Company,
    string? OfficeSize,
    string? Phone,
    string? Interest,
    string HeardAboutCode,
    string? HeardAboutDetail,
    string EntrySource,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    string? ReferrerDomain,
    string Language,
    string VisitorTimeZone,
    bool MarketingOptIn,
    DateTime CreatedAtUtc,
    DateTime? ConfirmedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancelledBy,
    string? CancelReason,
    DateTime? OutcomeAtUtc
);

public sealed record PublicBookingListResponse(
    IReadOnlyList<PublicBookingAdminItem> Items,
    int Total,
    int Page,
    int PageSize
);

/// <summary>Un renglón del resumen: la clave estable, su etiqueta (si la tiene) y cuántas.</summary>
public sealed record PublicBookingCount(string Key, string? Label, int Count);

public sealed record PublicBookingSummaryResponse(
    int Total,
    IReadOnlyList<PublicBookingCount> ByStatus,
    IReadOnlyList<PublicBookingCount> ByHeardAbout,
    IReadOnlyList<PublicBookingCount> BySource,
    IReadOnlyList<PublicBookingCount> ByUtmSource
);
