using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.ClientForms;

public static class FormActionFailedConsumer
{
    public static async Task Handle(
        FormActionFailedIntegrationEvent evt,
        ClientFormNotificationSender sender,
        IRecipientResolver resolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var resolved = await resolver.ResolveAsync(
                new Participants(evt.TenantId, [evt.ResponsibleUserId]),
                new RecipientValidation(
                    TasksPermissions.FormRequestsReview,
                    PermissionModuleMap.ModuleFor(TasksPermissions.FormRequestsReview)
                ),
                ct
            );
            if (resolved.IsFailure || !resolved.Value.Contains(evt.ResponsibleUserId))
                return;
            await sender.SendAsync(Notification(evt), ct);
        }
    }

    private static ClientFormNotificationRequest Notification(FormActionFailedIntegrationEvent evt) =>
        new(
            evt.TenantId,
            evt.ResponsibleUserId,
            evt.FormRequestId,
            evt.EventId,
            evt.OccurredOn,
            NotificationKinds.TasksFormActionFailed,
            "task.form_action_failed.v1",
            "task.form_action_failed.push.v1",
            "A form action needs attention",
            evt.ActionType,
            new Dictionary<string, object?> { ["action_type"] = evt.ActionType, ["error_code"] = evt.ErrorCode },
            new Dictionary<string, string> { ["actionType"] = evt.ActionType, ["errorCode"] = evt.ErrorCode },
            NotificationCategory.ClientRequests
        );
}
