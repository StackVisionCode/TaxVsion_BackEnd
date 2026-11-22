using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

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

            const string title = "File blocked";
            const string body = "A file in your office was blocked because it goes against our content policy.";
            foreach (var userId in recipients)
            {
                await dispatcher.DispatchAsync(
                    new NotificationIntent(
                        NotificationKinds.CloudStorageFileBlockedByPolicy,
                        evt.TenantId,
                        new NotificationTarget(userId),
                        evt.EventId,
                        "cloudstorage.file.blocked_by_policy.v1",
                        evt.OccurredOn,
                        correlation.CorrelationId,
                        InApp: new InAppContent(
                            ResourceId: evt.FileId.ToString(),
                            FallbackTitle: title,
                            FallbackBody: body
                        )
                    ),
                    ct
                );
            }
        }
    }
}
