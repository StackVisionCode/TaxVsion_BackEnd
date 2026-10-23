using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.ClientForms;

public static class FormAttachmentRejectedConsumer
{
    private const string TemplateKey = "task.form_attachment_rejected.v1";

    public static async Task Handle(
        FormAttachmentRejectedIntegrationEvent evt,
        ICustomerEmailDirectoryRepository customers,
        ICustomerContactPolicyRepository policies,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribe,
        NotificationDispatcher dispatcher,
        ICustomerAssignmentAudienceRepository assignments,
        IRecipientResolver recipients,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hosts,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            await NotifyCustomerAsync(evt, customers, policies, gateway, scribe, dispatcher, recipients, portal, hosts, correlation, ct);
            await NotifyStaffAsync(evt, dispatcher, assignments, recipients, correlation, ct);
        }
    }

    private static async Task NotifyCustomerAsync(
        FormAttachmentRejectedIntegrationEvent evt,
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
        if (evt.Participant.CustomerId is not { } customerId)
            return;
        var message = ClientMessage(evt.ReasonCode);
        await ClientFormCustomerNotifier.NotifyAsync(
            new ClientFormCustomerNotification(
                evt.TenantId, customerId, evt.FormRequestId, evt.EventId, evt.OccurredOn,
                NotificationKinds.TasksFormAttachmentRejected, TemplateKey,
                "A file needs your attention", message,
                new Dictionary<string, object?>
                {
                    ["request_title"] = evt.Title,
                    ["file_extension"] = evt.FileExtension,
                    ["client_message"] = message,
                },
                new Dictionary<string, string> { ["title"] = evt.Title }
            ),
            customers, policies, gateway, scribe, dispatcher, recipients, portal, hosts, correlation, ct
        );
    }

    private static async Task NotifyStaffAsync(
        FormAttachmentRejectedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICustomerAssignmentAudienceRepository assignments,
        IRecipientResolver resolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var recipients = await ClientFormStaffRecipients.ResolveRequesterForReadAsync(
            evt.TenantId, evt.Participant.CustomerId, evt.RequestedByUserId, assignments, resolver, ct
        );
        foreach (var userId in recipients)
        {
            var context = new Dictionary<string, string> { ["formRequestId"] = evt.FormRequestId.ToString() };
            if (evt.Participant.CustomerId is { } customerId)
                context["customerId"] = customerId.ToString();
            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"user:{userId:N}",
                "A client file was rejected",
                NotificationCategory.Collaboration,
                NotificationKinds.TasksFormAttachmentRejectedStaff,
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: userId,
                inApp: new InAppNotificationRequest(
                    NotificationKinds.TasksFormAttachmentRejectedStaff,
                    userId,
                    evt.EventId,
                    "task.form_attachment_rejected_staff.v1",
                    evt.OccurredOn,
                    "A client file was rejected",
                    FallbackBody: evt.Title,
                    ResourceId: evt.FormRequestId.ToString(),
                    ResourceContext: context,
                    Params: new Dictionary<string, string> { ["title"] = evt.Title },
                    GroupKey: $"form_request:{evt.FormRequestId:N}"
                ),
                ct: ct
            );
        }
    }

    private static string ClientMessage(string reasonCode) => reasonCode switch
    {
        "infected" => "The file couldn't be accepted because our security scan flagged it. Please upload a different copy.",
        "blocked_by_policy" or "scan_failed" => "The file type or content isn't allowed. Please upload a PDF or an image.",
        "not_owned" or "wrong_folder" => "The file couldn't be linked to this form. Please upload it again.",
        "upload_incomplete" => "The upload didn't finish. Please try again.",
        _ => "The file couldn't be accepted. Please upload it again.",
    };
}
