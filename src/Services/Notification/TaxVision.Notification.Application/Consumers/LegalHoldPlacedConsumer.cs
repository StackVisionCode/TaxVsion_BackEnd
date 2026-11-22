using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>C16-c: audiencia por capacidad `cloudstorage.legal.manage` (ver DMCA takedown).</summary>
public static class LegalHoldPlacedConsumer
{
    public static async Task Handle(
        LegalHoldPlacedIntegrationEvent evt,
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
                NotificationKinds.CloudStorageLegalHoldPlaced,
                evt.TenantId,
                new NotificationTarget(),
                evt.EventId,
                "cloudstorage.file.legal_hold_placed.v1",
                evt.OccurredOn,
                correlation.CorrelationId
            );
            // Correo obligatorio: no depende de quién publica la campana (T3.7).
            var variables = new Dictionary<string, object?>
            {
                ["placed_at"] = ScribeDates.Instant(evt.OccurredOn),
                ["action_url"] = await OfficeAlertEmails.FileUrlAsync(links, evt.TenantId, evt.FileId, ct),
            };
            if (!string.IsNullOrWhiteSpace(evt.Reason))
                variables["reason"] = evt.Reason;
            await OfficeAlertEmails.SendAsync(
                intent,
                "storage.legal_hold_placed.v1",
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
