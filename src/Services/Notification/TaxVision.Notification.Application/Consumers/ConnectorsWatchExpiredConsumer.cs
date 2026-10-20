using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
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

            var title = "Tu cuenta de correo necesita reconexión";
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
                    $"user:{userId:N}",
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
                        FallbackBody: $"Dejamos de recibir correo nuevo de tu cuenta {evt.ProviderCode}. Reconéctala para reanudar la sincronización.",
                        ResourceId: evt.AccountId.ToString(),
                        Params: new Dictionary<string, string> { ["providerCode"] = evt.ProviderCode }
                    ),
                    ct: ct
                );
            }
        }
    }
}
