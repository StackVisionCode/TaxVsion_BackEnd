using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

public static class CustomersBulkImportedConsumer
{
    public static async Task Handle(
        CustomersBulkImportedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IInAppSourceGate inAppSource,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (!inAppSource.PublishesInApp(InAppSourceGroup.CustomerImport))
                return;

            // El creador, salvo que Customer haya pasado el aviso a otro (creador retirado).
            var recipient = evt.NotifyUserId ?? evt.CreatedByUserId;
            var title = "Importación masiva completada";
            var body =
                $"{evt.SuccessCount}/{evt.TotalRows} clientes importados"
                + (evt.FailedCount > 0 ? $", {evt.FailedCount} con errores." : ".");

            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                NotificationRecipient.ForUser(recipient),
                title,
                NotificationCategory.Collaboration,
                "customer.bulk_imported",
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: recipient,
                inApp: new InAppNotificationRequest(
                    NotificationKinds.CustomerBulkImported,
                    recipient,
                    evt.EventId,
                    "customer.bulk_imported.v1",
                    evt.CompletedAtUtc,
                    title,
                    FallbackBody: body,
                    ResourceId: evt.ImportJobId.ToString(),
                    Params: new Dictionary<string, string>
                    {
                        ["totalRows"] = evt.TotalRows.ToString(),
                        ["successCount"] = evt.SuccessCount.ToString(),
                        ["failedCount"] = evt.FailedCount.ToString(),
                    }
                ),
                ct: ct
            );
        }
    }
}
