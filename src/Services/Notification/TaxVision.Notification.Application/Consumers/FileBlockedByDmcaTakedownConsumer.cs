using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

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
        PushLinks links,
        IRecipientLocale locales,
        IUserEmailResolver emails,
        IScribeRenderClient scribe,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
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

            if (recipients.Count == 0)
                return;

            var intent = new NotificationIntent(
                NotificationKinds.CloudStorageDmcaTakedown,
                evt.TenantId,
                new NotificationTarget(),
                evt.EventId,
                "cloudstorage.file.blocked_by_dmca_takedown.v1",
                evt.OccurredOn,
                correlation.CorrelationId
            );
            // Correo obligatorio: no depende de quién publica la campana (T3.7).
            var variables = new Dictionary<string, object?>
            {
                ["blocked_at"] = ScribeDates.Instant(evt.OccurredOn),
                ["notice_reference"] = evt.DmcaNoticeId.ToString(),
                ["action_url"] = await OfficeAlertEmails.FileUrlAsync(links, evt.TenantId, evt.FileId, ct),
            };
            await OfficeAlertEmails.SendAsync(
                intent,
                "storage.dmca_takedown.v1",
                variables,
                recipients,
                dispatcher,
                emails,
                scribe,
                locales,
                ct
            );

            if (!inAppSource.PublishesInApp(InAppSourceGroup.CloudStorageLegal))
                return;

            foreach (var userId in recipients)
            {
                await dispatcher.DispatchAsync(
                    intent with
                    {
                        Target = new NotificationTarget(userId),
                        InApp = new InAppContent(ResourceId: evt.FileId.ToString()),
                    },
                    ct
                );
            }
        }
    }
}
