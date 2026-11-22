using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

public static class CustomersBulkImportedConsumer
{
    public static async Task Handle(
        CustomersBulkImportedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            // El creador, salvo que Customer haya pasado el aviso a otro (creador retirado).
            var recipient = evt.NotifyUserId ?? evt.CreatedByUserId;
            var parameters = new Dictionary<string, string>
            {
                ["totalRows"] = evt.TotalRows.ToString(CultureInfo.InvariantCulture),
                ["successCount"] = evt.SuccessCount.ToString(CultureInfo.InvariantCulture),
                ["updatedCount"] = evt.UpdatedCount.ToString(CultureInfo.InvariantCulture),
                ["skippedCount"] = evt.SkippedCount.ToString(CultureInfo.InvariantCulture),
                ["failedCount"] = evt.FailedCount.ToString(CultureInfo.InvariantCulture),
            };
            // El cuerpo ({summary}) lo arma el frontend con los conteos; aquí queda sin cuerpo.
            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    NotificationKinds.CustomerBulkImported,
                    evt.TenantId,
                    new NotificationTarget(recipient),
                    evt.EventId,
                    "customer.bulk_imported.v1",
                    evt.CompletedAtUtc,
                    correlation.CorrelationId,
                    InApp: new InAppContent(Params: parameters, ResourceId: evt.ImportJobId.ToString())
                ),
                ct
            );
        }
    }
}
