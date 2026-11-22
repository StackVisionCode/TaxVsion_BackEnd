using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// Contranotificación DMCA presentada: avisa al equipo con <c>cloudstorage.legal.manage</c> (D-N45), misma audiencia
/// que el takedown. Correo obligatorio con los mismos datos que el del takedown, y campana. Node no consume este
/// evento, así que la campana no pasa por el gate de fuente in-app.
/// </summary>
public static class DmcaCounterNoticeSubmittedConsumer
{
    public static async Task Handle(
        DmcaCounterNoticeSubmittedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
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
                NotificationKinds.CloudStorageDmcaCounterNoticeSubmitted,
                evt.TenantId,
                new NotificationTarget(),
                evt.EventId,
                "cloudstorage.dmca.counter_notice_submitted.v1",
                evt.OccurredOn,
                correlation.CorrelationId
            );
            var variables = new Dictionary<string, object?>
            {
                ["submitted_at"] = ScribeDates.Instant(evt.OccurredOn),
                ["notice_reference"] = evt.DmcaNoticeId.ToString(),
                ["action_url"] = await OfficeAlertEmails.FileUrlAsync(links, evt.TenantId, evt.FileId, ct),
            };
            await OfficeAlertEmails.SendAsync(
                intent,
                "storage.dmca_counter_notice.v1",
                variables,
                recipients,
                dispatcher,
                emails,
                scribe,
                locales,
                ct
            );

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
