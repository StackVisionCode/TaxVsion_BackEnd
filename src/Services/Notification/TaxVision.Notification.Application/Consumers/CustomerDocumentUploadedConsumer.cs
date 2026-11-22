using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

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
                await dispatcher.DispatchAsync(
                    new NotificationIntent(
                        NotificationKinds.CloudStorageCustomerDocumentUploaded,
                        evt.TenantId,
                        new NotificationTarget(userId),
                        evt.EventId,
                        "cloudstorage.customer_document_uploaded.v1",
                        evt.OccurredOn,
                        correlation.CorrelationId,
                        InApp: new InAppContent(
                            Params: new Dictionary<string, string> { ["customerId"] = evt.CustomerId.ToString() },
                            ResourceId: evt.FileId.ToString(),
                            ResourceContext: context,
                            GroupKey: $"customer_uploads:{evt.CustomerId:N}:{evt.OccurredOn:yyyyMMdd}",
                            FallbackTitle: title
                        )
                    ),
                    ct
                );
            }
        }
    }
}
