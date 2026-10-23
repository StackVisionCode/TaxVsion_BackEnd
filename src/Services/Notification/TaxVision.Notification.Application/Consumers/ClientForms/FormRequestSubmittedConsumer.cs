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
        NotificationDispatcher dispatcher,
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
                await RecordAsync(evt, userId, dispatcher, correlation, ct);
        }
    }

    private static Task RecordAsync(
        FormRequestSubmittedIntegrationEvent evt,
        Guid userId,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var customerName = evt.CustomerDisplayName ?? "External participant";
        var context = new Dictionary<string, string> { ["formRequestId"] = evt.FormRequestId.ToString() };
        if (evt.Participant.CustomerId is { } customerId)
            context["customerId"] = customerId.ToString();
        return dispatcher.RecordInAppAsync(
            evt.TenantId,
            $"user:{userId:N}",
            $"{customerName} submitted a form",
            NotificationCategory.Collaboration,
            NotificationKinds.TasksFormRequestSubmitted,
            evt.EventId,
            correlation.CorrelationId,
            recipientUserId: userId,
            inApp: new InAppNotificationRequest(
                NotificationKinds.TasksFormRequestSubmitted,
                userId,
                evt.EventId,
                "task.form_request_submitted.v1",
                evt.OccurredOn,
                $"{customerName} submitted a form",
                FallbackBody: evt.Title,
                ResourceId: evt.FormRequestId.ToString(),
                ResourceContext: context,
                Params: new Dictionary<string, string>
                {
                    ["title"] = evt.Title,
                    ["customerName"] = customerName,
                    ["revisionNumber"] = evt.RevisionNumber.ToString(),
                },
                GroupKey: $"form_request:{evt.FormRequestId:N}"
            ),
            ct: ct
        );
    }
}
