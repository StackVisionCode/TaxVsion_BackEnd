using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Offboarding;

/// <summary>
/// Un buzón de oficina quedó ligado a un empleado retirado (es su dirección o lo conectó él). Avisa a la
/// audiencia del evento (<c>connectors.accounts.write</c>) para que lo reconecten o lo desconecten.
/// </summary>
public static class ConnectorsOfficeMailboxReviewRequiredConsumer
{
    public const string EmailEventKey = "connectors.office_mailbox_review_required.v1";

    public static async Task Handle(
        ConnectorsOfficeMailboxReviewRequiredIntegrationEvent evt,
        OffboardingNoticeSender sender,
        ICorrelationContext correlation,
        ILogger<ConnectorsOfficeMailboxReviewRequiredIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var permission = string.IsNullOrWhiteSpace(evt.AudiencePermission)
                ? ConnectorsPermissions.AccountsWrite
                : evt.AudiencePermission;
            var recipients = await sender.ByPermissionsAsync(evt.TenantId, [permission], ct);
            if (recipients.Count == 0)
            {
                logger.LogWarning(
                    "Office mailbox review for account {AccountId} has no active recipient.",
                    evt.AccountId
                );
                return;
            }

            var link = await sender.StaffLinkAsync(evt.TenantId, $"/email?accountId={evt.AccountId:D}", ct);

            await sender.SendAsync(
                new OffboardingNoticeContext(
                    evt.TenantId,
                    evt.EventId,
                    evt.OccurredOn,
                    "connectors.office_mailbox_review_required.v1",
                    NotificationKinds.ConnectorsOfficeMailboxReviewRequired,
                    NotificationCategory.Collaboration,
                    EmailEventKey,
                    link,
                    evt.AccountId.ToString(),
                    $"connectors.office_mailbox_review_required:{evt.AccountId:N}",
                    // Connectors puede volver a avisar lo mismo con otro EventId: un correo por buzón y retiro.
                    DeliveryKey: $"mailbox:{evt.AccountId:N}:{evt.OffboardedUserId:N}"
                ),
                [
                    new OffboardingNotice(
                        recipients,
                        $"Reconnect the office mailbox {evt.EmailAddress}",
                        Body(evt.Reason, evt.EmailAddress),
                        new Dictionary<string, object?>
                        {
                            ["mailbox_email"] = evt.EmailAddress,
                            ["reason"] = evt.Reason,
                            ["mailbox_url"] = link,
                        },
                        new Dictionary<string, string> { ["mailbox"] = evt.EmailAddress, ["reason"] = evt.Reason }
                    ),
                ],
                ct
            );
        }
    }

    private static string Body(string reason, string email)
    {
        const string Fix = "Reconnect it with an account your office controls, or disconnect it.";
        return reason switch
        {
            "AddressOfOffboardedUser" =>
                $"The office mailbox {email} is the address of someone who's no longer with your office. {Fix}",
            "ConnectedByOffboardedUser" =>
                $"The office mailbox {email} was connected by someone who's no longer with your office. {Fix}",
            _ => $"The office mailbox {email} is linked to someone who's no longer with your office. {Fix}",
        };
    }
}
