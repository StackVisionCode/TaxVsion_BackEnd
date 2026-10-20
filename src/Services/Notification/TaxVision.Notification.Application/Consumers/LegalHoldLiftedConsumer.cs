using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>C16-c: audiencia por capacidad `cloudstorage.legal.manage` (ver DMCA takedown).</summary>
public static class LegalHoldLiftedConsumer
{
    public static async Task Handle(
        LegalHoldLiftedIntegrationEvent evt,
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

            const string title = "Retencion legal levantada";
            const string body = "La retencion legal sobre un archivo del tenant fue levantada.";
            foreach (var userId in recipients)
            {
                await dispatcher.RecordInAppAsync(
                    evt.TenantId,
                    $"user:{userId:N}",
                    title,
                    NotificationCategory.StorageAndQuota,
                    "cloudstorage.file.legal_hold_lifted",
                    evt.EventId,
                    correlation.CorrelationId,
                    recipientUserId: userId,
                    inApp: new InAppNotificationRequest(
                        NotificationKinds.CloudStorageLegalHoldLifted,
                        userId,
                        evt.EventId,
                        "cloudstorage.file.legal_hold_lifted.v1",
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
