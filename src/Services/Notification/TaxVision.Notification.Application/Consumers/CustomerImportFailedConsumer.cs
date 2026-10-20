using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

public static class CustomerImportFailedConsumer
{
    public static async Task Handle(
        CustomerImportFailedIntegrationEvent evt,
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

            var title = "Importación masiva fallida";
            var body = string.IsNullOrWhiteSpace(evt.Reason)
                ? "La importación no pudo completarse."
                : $"La importación no pudo completarse: {evt.Reason}";

            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"user:{evt.CreatedByUserId:N}",
                title,
                NotificationCategory.Collaboration,
                "customer.import_failed",
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: evt.CreatedByUserId,
                inApp: new InAppNotificationRequest(
                    NotificationKinds.CustomerImportFailed,
                    evt.CreatedByUserId,
                    evt.EventId,
                    "customer.import_failed.v1",
                    evt.FailedAtUtc,
                    title,
                    FallbackBody: body,
                    ResourceId: evt.ImportJobId.ToString(),
                    Params: new Dictionary<string, string> { ["reason"] = evt.Reason }
                ),
                ct: ct
            );
        }
    }
}
