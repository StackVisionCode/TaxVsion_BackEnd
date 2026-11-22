using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

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

            // AutoCovered (ya había un link público) viaja como parámetro; el texto lo decide el archivo de textos.
            var parameters = new Dictionary<string, string> { ["autoCovered"] = evt.AutoCovered ? "true" : "false" };
            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    NotificationKinds.CloudStorageFolderItemAdded,
                    evt.TenantId,
                    new NotificationTarget(evt.CreatedByUserId),
                    evt.EventId,
                    "cloudstorage.sharelink.folder_item_added.v1",
                    evt.OccurredOn,
                    correlation.CorrelationId,
                    InApp: new InAppContent(
                        Params: parameters,
                        ResourceId: evt.FileId.ToString(),
                        ResourceContext: new Dictionary<string, string> { ["folderId"] = evt.FolderId.ToString() }
                    )
                ),
                ct
            );
        }
    }
}
