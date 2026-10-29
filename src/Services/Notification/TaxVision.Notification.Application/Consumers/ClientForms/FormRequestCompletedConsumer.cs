using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.ClientForms;

public static class FormRequestCompletedConsumer
{
    private const string TemplateKey = "task.form_request_completed.v1";

    public static async Task Handle(
        FormRequestCompletedIntegrationEvent evt,
        ClientFormNotificationSender sender,
        IRecipientResolver recipients,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (evt.Participant.CustomerId is not { } customerId)
                return;
            await ClientFormCustomerNotifier.NotifyAsync(Notification(evt, customerId), sender, recipients, ct);
        }
    }

    private static ClientFormCustomerNotification Notification(
        FormRequestCompletedIntegrationEvent evt,
        Guid customerId
    ) =>
        new(
            evt.TenantId,
            customerId,
            evt.FormRequestId,
            evt.EventId,
            evt.OccurredOn,
            NotificationKinds.TasksFormRequestCompleted,
            TemplateKey,
            "Your form was received",
            evt.Title,
            new Dictionary<string, object?> { ["request_title"] = evt.Title },
            new Dictionary<string, string> { ["title"] = evt.Title, ["revisionNumber"] = evt.RevisionNumber.ToString() }
        );
}
