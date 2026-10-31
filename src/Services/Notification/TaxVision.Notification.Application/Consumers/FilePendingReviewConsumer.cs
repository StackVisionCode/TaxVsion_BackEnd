using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// `cloudstorage.file.pending_review.v1` (T-20): avisa solo a quien subió el archivo que quedó en
/// revisión de contenido. Mismo gate T3.7 que el bloqueo (<see cref="InAppSourceGroup.CloudStoragePolicy"/>);
/// con el default `Communication` la fila la crea Communication. El motivo con la puntuación nunca sale.
/// </summary>
public static class FilePendingReviewConsumer
{
    public static async Task Handle(
        FilePendingReviewIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IInAppSourceGate inAppSource,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (!inAppSource.PublishesInApp(InAppSourceGroup.CloudStoragePolicy) || evt.CreatedBy is not { } userId)
                return;

            const string title = "File under review";
            const string body =
                "One of your files is being reviewed against our content policy. It will be available once approved.";
            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"user:{userId:N}",
                title,
                NotificationCategory.StorageAndQuota,
                NotificationKinds.CloudStorageFilePendingReview,
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: userId,
                inApp: new InAppNotificationRequest(
                    NotificationKinds.CloudStorageFilePendingReview,
                    userId,
                    evt.EventId,
                    "cloudstorage.file.pending_review.v1",
                    evt.OccurredOn,
                    title,
                    FallbackBody: body,
                    ResourceId: evt.FileId.ToString()
                ),
                ct: ct
            );
        }
    }
}
