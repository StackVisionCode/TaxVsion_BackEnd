using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>C16-c: audiencia por capacidad `cloudstorage.legal.manage` (ver DMCA takedown).</summary>
public static class LegalHoldPlacedConsumer
{
    public static async Task Handle(
        LegalHoldPlacedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IInAppSourceGate inAppSource,
        IRecipientResolver recipientResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (!inAppSource.PublishesInApp(InAppSourceGroup.CloudStorageLegal))
                return;

            var recipients = await OfficeAlertRecipients.ResolveOwnerAndSubscribersAsync(
                evt.TenantId,
                NotificationPermissions.StorageAlerts,
                new RecipientValidation(
                    RequiredCapability: CloudStoragePermissions.LegalManage,
                    RequiredModule: PermissionModuleMap.ModuleFor(CloudStoragePermissions.LegalManage),
                    ExcludeActorUserId: evt.CreatedBy
                ),
                recipientResolver,
                ct
            );

            const string title = "Archivo en retencion legal";
            const string body = "Un archivo del tenant quedo bajo retencion legal y no puede borrarse mientras dure.";
            foreach (var userId in recipients)
            {
                await dispatcher.RecordInAppAsync(
                    evt.TenantId,
                    $"user:{userId:N}",
                    title,
                    NotificationCategory.StorageAndQuota,
                    "cloudstorage.file.legal_hold_placed",
                    evt.EventId,
                    correlation.CorrelationId,
                    recipientUserId: userId,
                    inApp: new InAppNotificationRequest(
                        NotificationKinds.CloudStorageLegalHoldPlaced,
                        userId,
                        evt.EventId,
                        "cloudstorage.file.legal_hold_placed.v1",
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
}
