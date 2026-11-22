using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>C16-c: audiencia por capacidad `cloudstorage.legal.manage` (ver DMCA takedown).</summary>
public static class FileReinstatedFromTakedownConsumer
{
    public static async Task Handle(
        FileReinstatedFromTakedownIntegrationEvent evt,
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
                NotificationKinds.CloudStorageDmcaReinstated,
                evt.TenantId,
                new NotificationTarget(),
                evt.EventId,
                "cloudstorage.file.reinstated_from_takedown.v1",
                evt.OccurredOn,
                correlation.CorrelationId
            );
            // Correo obligatorio: no depende de quién publica la campana (T3.7).
            var variables = new Dictionary<string, object?>
            {
                ["reinstated_at"] = ScribeDates.Instant(evt.OccurredOn),
                ["notice_reference"] = evt.DmcaNoticeId.ToString(),
                ["action_url"] = await OfficeAlertEmails.FileUrlAsync(links, evt.TenantId, evt.FileId, ct),
            };
            await OfficeAlertEmails.SendAsync(
                intent,
                "storage.dmca_reinstated.v1",
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
