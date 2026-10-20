using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Common;

internal static class BillingNotificationPublisher
{
    public static async Task RecordAsync(
        NotificationDispatcher dispatcher,
        IReadOnlyList<Guid> recipients,
        Guid tenantId,
        Guid eventId,
        string correlationId,
        string kind,
        string sourceEventType,
        DateTime occurredOn,
        string title,
        string? body,
        string resourceId,
        IReadOnlyDictionary<string, string> parameters,
        string? groupKey,
        CancellationToken ct
    )
    {
        foreach (var userId in recipients)
        {
            await dispatcher.RecordInAppAsync(
                tenantId,
                $"user:{userId:N}",
                title,
                NotificationCategory.Billing,
                kind,
                eventId,
                correlationId,
                recipientUserId: userId,
                inApp: new InAppNotificationRequest(
                    kind,
                    userId,
                    eventId,
                    sourceEventType,
                    occurredOn,
                    title,
                    FallbackBody: body,
                    ResourceId: resourceId,
                    Params: parameters,
                    GroupKey: groupKey
                ),
                ct: ct
            );
        }
    }
}
