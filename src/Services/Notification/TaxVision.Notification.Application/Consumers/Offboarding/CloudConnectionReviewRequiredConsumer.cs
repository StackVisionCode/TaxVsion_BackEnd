using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Offboarding;

/// <summary>
/// Una conexión de nube de la oficina (Drive/OneDrive/Dropbox) quedó ligada a un empleado retirado.
/// Avisa a la audiencia del evento (<c>cloudstorage.settings.manage</c>) para que la reconecten con
/// una cuenta de la oficina o la desconecten.
/// </summary>
public static class CloudConnectionReviewRequiredConsumer
{
    public const string EmailEventKey = "storage.cloud_connection_review_required.v1";

    public static async Task Handle(
        CloudConnectionReviewRequiredIntegrationEvent evt,
        OffboardingNoticeSender sender,
        ICorrelationContext correlation,
        ILogger<CloudConnectionReviewRequiredIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var permission = string.IsNullOrWhiteSpace(evt.AudiencePermission)
                ? CloudStoragePermissions.SettingsManage
                : evt.AudiencePermission;
            var recipients = await sender.ByPermissionsAsync(evt.TenantId, [permission], ct);
            if (recipients.Count == 0)
            {
                logger.LogWarning(
                    "Cloud connection review for connection {ConnectionId} has no active recipient.",
                    evt.ConnectionId
                );
                return;
            }

            var provider = ProviderName(evt.Provider);
            var link = await sender.StaffLinkAsync(evt.TenantId, "/storage", ct);

            await sender.SendAsync(
                new OffboardingNoticeContext(
                    evt.TenantId,
                    evt.EventId,
                    evt.OccurredOn,
                    "cloudstorage.connection_review_required.v1",
                    NotificationKinds.CloudStorageConnectionReviewRequired,
                    NotificationCategory.StorageAndQuota,
                    EmailEventKey,
                    link,
                    evt.ConnectionId.ToString(),
                    $"cloudstorage.connection_review_required:{evt.ConnectionId:N}",
                    // CloudStorage puede volver a avisar lo mismo con otro EventId: un correo por conexión y retiro.
                    DeliveryKey: $"connection:{evt.ConnectionId:N}:{evt.OffboardedUserId:N}"
                ),
                [
                    new OffboardingNotice(
                        recipients,
                        $"Reconnect your office's {provider} connection",
                        Body(evt.Reason, provider, evt.AccountEmail),
                        new Dictionary<string, object?>
                        {
                            ["provider_name"] = provider,
                            ["account_email"] = evt.AccountEmail,
                            ["reason"] = evt.Reason,
                            ["storage_url"] = link,
                        },
                        new Dictionary<string, string> { ["provider"] = provider, ["reason"] = evt.Reason }
                    ),
                ],
                ct
            );
        }
    }

    internal static string ProviderName(string provider) =>
        provider?.Trim().ToLowerInvariant() switch
        {
            "google-drive" => "Google Drive",
            "onedrive" => "OneDrive",
            "dropbox" => "Dropbox",
            _ => "cloud storage",
        };

    private static string Body(string reason, string provider, string email)
    {
        const string Fix = "Reconnect it with an account that belongs to your office, or disconnect it.";
        return reason switch
        {
            "AccountOfOffboardedUser" =>
                $"Your office's {provider} connection uses the account {email}, which belongs to someone who's no longer with your office. {Fix}",
            "ConnectedByOffboardedUser" =>
                $"Your office's {provider} connection ({email}) was set up by someone who's no longer with your office. {Fix}",
            _ =>
                $"Your office's {provider} connection ({email}) is linked to someone who's no longer with your office. {Fix}",
        };
    }
}
