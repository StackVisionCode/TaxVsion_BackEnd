using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// C16-c: antes notificaba solo al uploader (`evt.CreatedBy`); ahora resuelve
/// audiencia por capacidad `cloudstorage.legal.manage` + owner, con suscripcion
/// `notifications.storage.alerts`. Un reclamo DMCA es trabajo del equipo legal
/// del tenant, no del uploader; el uploader queda enterado via el canal
/// de archivo (el archivo aparece bloqueado).
/// </summary>
public static class FileBlockedByDmcaTakedownConsumer
{
    public static async Task Handle(
        FileBlockedByDmcaTakedownIntegrationEvent evt,
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

            const string title = "Archivo bloqueado por reclamo DMCA";
            const string body = "Un archivo del tenant fue bloqueado tras recibirse un reclamo de derechos de autor.";
            foreach (var userId in recipients)
            {
                await dispatcher.RecordInAppAsync(
                    evt.TenantId,
                    $"user:{userId:N}",
                    title,
                    NotificationCategory.StorageAndQuota,
                    "cloudstorage.file.blocked_by_dmca_takedown",
                    evt.EventId,
                    correlation.CorrelationId,
                    recipientUserId: userId,
                    inApp: new InAppNotificationRequest(
                        NotificationKinds.CloudStorageDmcaTakedown,
                        userId,
                        evt.EventId,
                        "cloudstorage.file.blocked_by_dmca_takedown.v1",
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
