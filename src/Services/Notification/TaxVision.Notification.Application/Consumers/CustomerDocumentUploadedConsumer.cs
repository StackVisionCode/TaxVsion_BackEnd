using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

public static class CustomerDocumentUploadedConsumer
{
    public static async Task Handle(
        CustomerDocumentUploadedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICustomerAssignmentAudienceRepository assignments,
        IRecipientResolver resolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var assigned = await assignments.GetAssignedUserIdsAsync(evt.TenantId, evt.CustomerId, ct);
            if (assigned.Count == 0)
                return;

            var recipients = await resolver.ResolveAsync(
                new Participants(evt.TenantId, assigned.ToArray()),
                new RecipientValidation(
                    RequiredCapability: CloudStoragePermissions.FileView,
                    RequiredModule: PermissionModuleMap.ModuleFor(CloudStoragePermissions.FileView)
                ),
                ct
            );
            if (recipients.IsFailure)
                return;

            const string title = "Client uploaded a document";
            var context = new Dictionary<string, string>
            {
                ["customerId"] = evt.CustomerId.ToString(),
                ["fileId"] = evt.FileId.ToString(),
            };
            if (evt.FolderId is { } folderId)
                context["folderId"] = folderId.ToString();

            foreach (var userId in recipients.Value)
            {
                await dispatcher.RecordInAppAsync(
                    evt.TenantId,
                    $"user:{userId:N}",
                    title,
                    NotificationCategory.DocumentsAndSignatures,
                    "cloudstorage.customer_document_uploaded",
                    evt.EventId,
                    correlation.CorrelationId,
                    recipientUserId: userId,
                    inApp: new InAppNotificationRequest(
                        NotificationKinds.CloudStorageCustomerDocumentUploaded,
                        userId,
                        evt.EventId,
                        "cloudstorage.customer_document_uploaded.v1",
                        evt.OccurredOn,
                        title,
                        ResourceId: evt.FileId.ToString(),
                        ResourceContext: context,
                        Params: new Dictionary<string, string> { ["customerId"] = evt.CustomerId.ToString() },
                        GroupKey: $"customer_uploads:{evt.CustomerId:N}:{evt.OccurredOn:yyyyMMdd}"
                    ),
                    ct: ct
                );
            }
        }
    }
}
