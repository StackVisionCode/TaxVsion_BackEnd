using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Domain.Catalog;

public sealed record NotificationCatalogEntry(
    string Kind,
    NotificationCatalogCategory Category,
    NotificationPriority DefaultPriority,
    NotificationRecipientPolicy RecipientPolicy,
    IReadOnlySet<NotificationChannel> AllowedChannels,
    bool IsMandatory,
    string TitleKey,
    string? BodyKey,
    NotificationResourceType ResourceType,
    NotificationAction Action,
    // Interruptor de preferencias que apaga este aviso.
    NotificationCategory PreferenceCategory,
    IReadOnlySet<NotificationChannel>? MandatoryChannels = null
)
{
    public bool IsMandatoryFor(NotificationChannel channel) =>
        IsMandatory && (MandatoryChannels ?? AllowedChannels).Contains(channel);
}
