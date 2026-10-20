using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

public static class ConnectorsOAuthRefreshFailedConsumer
{
    public static async Task Handle(
        ConnectorsOAuthRefreshFailedIntegrationEvent evt,
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
                    "connectors.oauth.refresh_failed",
                    evt.EventId,
                    correlation.CorrelationId,
                    recipientUserId: userId,
                    inApp: new InAppNotificationRequest(
                        NotificationKinds.ConnectorsOAuthRefreshFailed,
                        userId,
                        evt.EventId,
                        "connectors.oauth.refresh_failed.v1",
                        evt.FailedAtUtc,
                        title,
                        FallbackBody: $"No pudimos renovar el acceso a tu cuenta {evt.ProviderCode}. Reconéctala para seguir recibiendo correo.",
                        ResourceId: evt.AccountId.ToString(),
                        Params: new Dictionary<string, string> { ["providerCode"] = evt.ProviderCode }
                    ),
                    ct: ct
                );
            }
        }
    }
}
