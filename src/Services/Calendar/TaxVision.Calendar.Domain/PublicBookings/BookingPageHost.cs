using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.PublicBookings;

/// <summary>Usuario de la plataforma que atiende demos de una página, con su enlace de reunión.</summary>
public sealed class BookingPageHost : BaseEntity
{
    public const int MaxDisplayNameLength = 200;

    private BookingPageHost() { }

    public Guid BookingPageId { get; private set; }

    [UserReference(
        UserReferenceKind.ActiveWork,
        HandledBy = "PublicBookingHostOffboardedConsumer",
        Note = "Se desactiva."
    )]
    public Guid UserId { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;
    public string NotificationEmail { get; private set; } = string.Empty;
    public string MeetingUrl { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    internal static BookingPageHost Create(Guid bookingPageId, Guid userId) =>
        new() { BookingPageId = bookingPageId, UserId = userId };

    internal void Apply(string displayName, string notificationEmail, string meetingUrl, int sortOrder, bool isActive)
    {
        DisplayName = displayName;
        NotificationEmail = notificationEmail;
        MeetingUrl = meetingUrl;
        SortOrder = sortOrder;
        IsActive = isActive;
    }

    internal void Deactivate() => IsActive = false;
}
