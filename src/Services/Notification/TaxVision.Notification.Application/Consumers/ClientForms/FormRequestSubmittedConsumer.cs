using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.ClientForms;

public static class FormRequestSubmittedConsumer
{
    public static async Task Handle(
        FormRequestSubmittedIntegrationEvent evt,
        ClientFormNotificationSender sender,
        ICustomerAssignmentAudienceRepository assignments,
        IRecipientResolver resolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var recipients = await ClientFormStaffRecipients.ResolveForReviewAsync(
                evt.TenantId,
                evt.Participant.CustomerId,
                evt.RequestedByUserId,
                evt.ResponsibleUserId,
                assignments,
                resolver,
                ct
            );
            foreach (var userId in recipients)
                await sender.SendAsync(Notification(evt, userId), ct);
        }
    }

    private static ClientFormNotificationRequest Notification(FormRequestSubmittedIntegrationEvent evt, Guid userId)
    {
        var customerName = evt.CustomerDisplayName ?? "External participant";
        return new ClientFormNotificationRequest(
            evt.TenantId,
            userId,
            evt.FormRequestId,
            evt.EventId,
            evt.OccurredOn,
            NotificationKinds.TasksFormRequestSubmitted,
            "task.form_request_submitted.v1",
            "task.form_request_submitted.push.v1",
            $"{customerName} submitted a form",
            evt.Title,
            new Dictionary<string, object?>
            {
                ["request_title"] = evt.Title,
                ["customer_name"] = customerName,
                ["revision_number"] = evt.RevisionNumber,
            },
            new Dictionary<string, string>
            {
                ["title"] = evt.Title,
                ["customerName"] = customerName,
                ["revisionNumber"] = evt.RevisionNumber.ToString(),
            },
            NotificationCategory.ClientRequests,
            evt.Participant.CustomerId
        );
    }
}
