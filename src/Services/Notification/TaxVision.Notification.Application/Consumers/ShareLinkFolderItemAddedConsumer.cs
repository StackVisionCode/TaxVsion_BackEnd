using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

public static class ShareLinkFolderItemAddedConsumer
{
    public static async Task Handle(
        ShareLinkFolderItemAddedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IInAppSourceGate inAppSource,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (!inAppSource.PublishesInApp(InAppSourceGroup.CloudStorageLegal))
                return;

            var title = "Archivo movido a una carpeta compartida";
            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"user:{evt.CreatedByUserId:N}",
                title,
                NotificationCategory.StorageAndQuota,
                "cloudstorage.sharelink.folder_item_added",
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: evt.CreatedByUserId,
                inApp: new InAppNotificationRequest(
                    NotificationKinds.CloudStorageFolderItemAdded,
                    evt.CreatedByUserId,
                    evt.EventId,
                    "cloudstorage.sharelink.folder_item_added.v1",
                    evt.OccurredOn,
                    title,
                    FallbackBody: evt.AutoCovered
                        ? "Un archivo tuyo quedó accesible por un link público existente al moverse de carpeta."
                        : "Un archivo tuyo se movió a una carpeta con un link público.",
                    ResourceId: evt.FileId.ToString(),
                    ResourceContext: new Dictionary<string, string> { ["folderId"] = evt.FolderId.ToString() }
                ),
                ct: ct
            );
        }
    }
}
