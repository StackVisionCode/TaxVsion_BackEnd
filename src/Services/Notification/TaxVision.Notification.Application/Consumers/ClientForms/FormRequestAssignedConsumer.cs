using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.ClientForms;

public static class FormRequestAssignedConsumer
{
    private const string TemplateKey = "task.form_request_assigned.v1";

    public static async Task Handle(
        FormRequestAssignedIntegrationEvent evt,
        ICustomerEmailDirectoryRepository customers,
        ICustomerContactPolicyRepository policies,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribe,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipients,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hosts,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (evt.Participant.CustomerId is not { } customerId)
                return;
            await ClientFormCustomerNotifier.NotifyAsync(
                Notification(evt, customerId),
                customers,
                policies,
                gateway,
                scribe,
                dispatcher,
                recipients,
                portal,
                hosts,
                correlation,
                ct
            );
        }
    }

    private static ClientFormCustomerNotification Notification(
        FormRequestAssignedIntegrationEvent evt,
        Guid customerId
    ) =>
        new(
            evt.TenantId,
            customerId,
            evt.FormRequestId,
            evt.EventId,
            evt.OccurredOn,
            NotificationKinds.TasksFormRequestAssigned,
            TemplateKey,
            "Your tax office requested information",
            evt.Title,
            new Dictionary<string, object?>
            {
                ["request_title"] = evt.Title,
                ["has_message"] = evt.HasMessage,
                ["due_date"] = evt.DueAtUtc,
            },
            new Dictionary<string, string> { ["title"] = evt.Title }
        );
}
