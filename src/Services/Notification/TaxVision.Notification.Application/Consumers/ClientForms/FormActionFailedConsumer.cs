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
        NotificationDispatcher dispatcher,
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
            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"user:{evt.ResponsibleUserId:N}",
                "A form action needs attention",
                NotificationCategory.Collaboration,
                NotificationKinds.TasksFormActionFailed,
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: evt.ResponsibleUserId,
                inApp: new InAppNotificationRequest(
                    NotificationKinds.TasksFormActionFailed,
                    evt.ResponsibleUserId,
                    evt.EventId,
                    "task.form_action_failed.v1",
                    evt.OccurredOn,
                    "A form action needs attention",
                    FallbackBody: evt.ActionType,
                    ResourceId: evt.FormRequestId.ToString(),
                    ResourceContext: new Dictionary<string, string>
                    {
                        ["formRequestId"] = evt.FormRequestId.ToString(),
                    },
                    Params: new Dictionary<string, string>
                    {
                        ["actionType"] = evt.ActionType,
                        ["errorCode"] = evt.ErrorCode,
                    },
                    GroupKey: $"form_request:{evt.FormRequestId:N}"
                ),
                ct: ct
            );
        }
    }
}
