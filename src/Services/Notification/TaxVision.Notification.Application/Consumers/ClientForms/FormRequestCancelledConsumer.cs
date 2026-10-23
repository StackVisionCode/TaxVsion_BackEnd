using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.ClientForms;

public static class FormRequestCancelledConsumer
{
    public static async Task Handle(
        FormRequestCancelledIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipients,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (!evt.NotifyClient || evt.Participant.CustomerId is not { } customerId)
                return;
            await ClientFormCustomerNotifier.NotifyInAppAsync(
                new ClientFormCustomerNotification(
                    evt.TenantId, customerId, evt.FormRequestId, evt.EventId, evt.OccurredOn,
                    NotificationKinds.TasksFormRequestCancelled, "task.form_request_cancelled.v1",
                    "A form request was cancelled", evt.Title,
                    new Dictionary<string, object?>(),
                    new Dictionary<string, string> { ["title"] = evt.Title }
                ),
                dispatcher,
                recipients,
                correlation,
                ct
            );
        }
    }
}
