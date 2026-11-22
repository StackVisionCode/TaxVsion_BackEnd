using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

public static class CustomerImportFailedConsumer
{
    public static async Task Handle(
        CustomerImportFailedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            // El creador, salvo que Customer haya pasado el aviso a otro (creador retirado).
            var recipient = evt.NotifyUserId ?? evt.CreatedByUserId;
            var parameters = new Dictionary<string, string> { ["reason"] = evt.Reason };
            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    NotificationKinds.CustomerImportFailed,
                    evt.TenantId,
                    new NotificationTarget(recipient),
                    evt.EventId,
                    "customer.import_failed.v1",
                    evt.FailedAtUtc,
                    correlation.CorrelationId,
                    InApp: new InAppContent(Params: parameters, ResourceId: evt.ImportJobId.ToString())
                ),
                ct
            );
        }
    }
}
