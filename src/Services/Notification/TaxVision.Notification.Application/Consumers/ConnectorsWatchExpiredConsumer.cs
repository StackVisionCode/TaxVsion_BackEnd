using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

public static class ConnectorsWatchExpiredConsumer
{
    public static async Task Handle(
        ConnectorsWatchExpiredIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IInAppSourceGate inAppSource,
        IRecipientResolver recipientResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (!inAppSource.PublishesInApp(InAppSourceGroup.Connectors))
                return;

            var parameters = new Dictionary<string, string> { ["providerCode"] = evt.ProviderCode };
            var (title, body) = InAppFallback.For(NotificationKinds.ConnectorsWatchExpired, parameters);
            var recipients = await MailboxAlertRecipients.ResolveAsync(
                evt.TenantId,
                evt.CreatedByUserId,
                evt.IsOfficeMailbox,
                evt.MailboxOwnerUserId,
                recipientResolver,
                ct
            );

            foreach (var userId in recipients)
            {
                await dispatcher.RecordInAppAsync(
                    evt.TenantId,
                    NotificationRecipient.ForUser(userId),
                    title,
                    NotificationCategory.Collaboration,
                    "connectors.watch.expired",
                    evt.EventId,
                    correlation.CorrelationId,
                    recipientUserId: userId,
                    inApp: new InAppNotificationRequest(
                        NotificationKinds.ConnectorsWatchExpired,
                        userId,
                        evt.EventId,
                        "connectors.watch.expired.v1",
                        evt.ExpiredAtUtc,
                        title,
                        FallbackBody: body,
                        ResourceId: evt.AccountId.ToString(),
                        Params: parameters
                    ),
                    ct: ct
                );
            }
        }
    }
}
