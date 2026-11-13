namespace TaxVision.Calendar.Domain.PublicBookings;

/// <summary>Un anfitrión tal como lo pide la configuración: se valida al entrar a la página.</summary>
public sealed record BookingPageHostInput(
    Guid UserId,
    string? DisplayName,
    string? NotificationEmail,
    string? MeetingUrl,
    int SortOrder,
    bool IsActive
);
