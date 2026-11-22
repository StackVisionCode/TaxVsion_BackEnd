using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

// Avisos de almacenamiento a owner + suscriptores de alertas: campana y push por destinatario; el archivo
// infectado lleva además su correo obligatorio.
public static class FileInfectedDetectedConsumer
{
    public static async Task Handle(
        FileInfectedDetectedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        PushLinks links,
        IRecipientLocale locales,
        IRecipientResolver recipientResolver,
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
                    RequiredCapability: CloudStoragePermissions.SettingsManage,
                    RequiredModule: PermissionModuleMap.ModuleFor(CloudStoragePermissions.SettingsManage)
                ),
                recipientResolver,
                ct
            );
            if (recipients.Count == 0)
                return;

            var intent = new NotificationIntent(
                NotificationKinds.CloudStorageFileInfected,
                evt.TenantId,
                new NotificationTarget(),
                evt.EventId,
                "cloudstorage.file.infected.v1",
                evt.OccurredOn,
                correlation.CorrelationId
            );
            var variables = new Dictionary<string, object?>
            {
                ["detected_at"] = ScribeDates.Instant(evt.OccurredOn),
                ["action_url"] = await OfficeAlertEmails.FileUrlAsync(links, evt.TenantId, evt.FileId, ct),
            };
            if (!string.IsNullOrWhiteSpace(evt.ScanReport))
                variables["scan_result"] = evt.ScanReport;
            await OfficeAlertEmails.SendAsync(
                intent,
                "storage.file_infected.v1",
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
                        InApp = new InAppContent(
                            ResourceId: evt.FileId.ToString(),
                            TemplateKey: "cloudstorage.file_infected"
                        ),
                        Push = new PushContent(),
                    },
                    ct
                );
            }
        }
    }
}

public static class StorageLimitExceededConsumer
{
    public static async Task Handle(
        StorageLimitExceededIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipientResolver,
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
                    RequiredCapability: CloudStoragePermissions.SettingsManage,
                    RequiredModule: PermissionModuleMap.ModuleFor(CloudStoragePermissions.SettingsManage)
                ),
                recipientResolver,
                ct
            );
            foreach (var userId in recipients)
            {
                await dispatcher.DispatchAsync(
                    new NotificationIntent(
                        NotificationKinds.CloudStorageStorageLimitExceeded,
                        evt.TenantId,
                        new NotificationTarget(userId),
                        evt.EventId,
                        "cloudstorage.storage_limit_exceeded.v1",
                        evt.OccurredOn,
                        correlation.CorrelationId,
                        InApp: new InAppContent(TemplateKey: "cloudstorage.quota_exceeded"),
                        Push: new PushContent()
                    ),
                    ct
                );
            }
        }
    }
}
