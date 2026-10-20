using TaxVision.Notification.Domain.Notifications;

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
    NotificationAction Action
);
