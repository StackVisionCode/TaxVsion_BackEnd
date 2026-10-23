using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.ClientForms;

public static class FormRequestReminderDueConsumer
{
    private const string TemplateKey = "task.form_request_reminder.v1";

    public static async Task Handle(
        FormRequestReminderDueIntegrationEvent evt,
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
                customers, policies, gateway, scribe, dispatcher, recipients, portal, hosts, correlation, ct
            );
        }
    }

    private static ClientFormCustomerNotification Notification(FormRequestReminderDueIntegrationEvent evt, Guid customerId) => new(
        evt.TenantId, customerId, evt.FormRequestId, evt.EventId, evt.OccurredOn,
        NotificationKinds.TasksFormRequestReminder, TemplateKey,
        evt.IsOverdue ? "Your form is overdue" : "Form reminder", evt.Title,
        new Dictionary<string, object?>
        {
            ["request_title"] = evt.Title,
            ["due_date"] = evt.DueAtUtc,
            ["is_overdue"] = evt.IsOverdue,
        },
        new Dictionary<string, string>
        {
            ["title"] = evt.Title,
            ["isOverdue"] = evt.IsOverdue.ToString(),
        }
    );
}
