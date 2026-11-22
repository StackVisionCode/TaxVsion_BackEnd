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
            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    kind,
                    tenantId,
                    new NotificationTarget(userId),
                    eventId,
                    sourceEventType,
                    occurredOn,
                    correlationId,
                    InApp: new InAppContent(
                        Params: parameters,
                        ResourceId: resourceId,
                        GroupKey: groupKey,
                        FallbackTitle: title,
                        FallbackBody: body
                    )
                ),
                ct
            );
        }
    }
}
