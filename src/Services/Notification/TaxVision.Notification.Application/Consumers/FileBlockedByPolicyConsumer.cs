using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// Fan-out para `cloudstorage.file.blocked_by_policy.v1` a owner + suscriptores de
/// alertas de almacenamiento, mismo patron que FileInfected/StorageLimitExceeded —
/// antes vivia en Communication (Node) notificando solo al uploader. Gate T3.7 por
/// grupo <see cref="InAppSourceGroup.CloudStoragePolicy"/>: default `Communication`
/// (sin regresion) y <c>Notifications__InApp__CloudStoragePolicy__Source=Notification</c>
/// activa el pipeline nuevo.
/// </summary>
public static class FileBlockedByPolicyConsumer
{
    public static async Task Handle(
        FileBlockedByPolicyIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IInAppSourceGate inAppSource,
        IRecipientResolver recipientResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (!inAppSource.PublishesInApp(InAppSourceGroup.CloudStoragePolicy))
                return;

            var recipients = await OfficeAlertRecipients.ResolveOwnerAndSubscribersAsync(
                evt.TenantId,
                NotificationPermissions.StorageAlerts,
                new RecipientValidation(
                    RequiredCapability: CloudStoragePermissions.SettingsManage,
                    RequiredModule: PermissionModuleMap.ModuleFor(CloudStoragePermissions.SettingsManage),
                    ExcludeActorUserId: evt.CreatedBy
                ),
                recipientResolver,
                ct
            );

            const string title = "Archivo bloqueado por moderacion";
            const string body = "Un archivo del tenant fue bloqueado tras no superar la revision de contenido.";
            foreach (var userId in recipients)
            {
                await dispatcher.RecordInAppAsync(
                    evt.TenantId,
                    $"user:{userId:N}",
                    title,
                    NotificationCategory.StorageAndQuota,
                    "cloudstorage.file.blocked_by_policy",
                    evt.EventId,
                    correlation.CorrelationId,
                    recipientUserId: userId,
                    inApp: new InAppNotificationRequest(
                        NotificationKinds.CloudStorageFileBlockedByPolicy,
                        userId,
                        evt.EventId,
                        "cloudstorage.file.blocked_by_policy.v1",
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
