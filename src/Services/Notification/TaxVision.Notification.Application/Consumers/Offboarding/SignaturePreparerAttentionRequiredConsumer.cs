using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Offboarding;

/// <summary>
/// Una solicitud de firma necesita que alguien actúe por el retiro de un empleado. Va a
/// <c>RecipientUserId</c> si viene y sigue activo; si no, a la audiencia <c>AudiencePermission</c>
/// (nunca queda en silencio). El texto depende del motivo; el <c>Detail</c> del evento no se muestra.
/// </summary>
public static class SignaturePreparerAttentionRequiredConsumer
{
    public const string EmailEventKey = "sig.preparer_attention_required.v1";

    public static async Task Handle(
        SignaturePreparerAttentionRequiredIntegrationEvent evt,
        OffboardingNoticeSender sender,
        ICorrelationContext correlation,
        ILogger<SignaturePreparerAttentionRequiredIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var recipients = await ResolveRecipientsAsync(evt, sender, ct);
            if (recipients.Count == 0)
            {
                logger.LogWarning(
                    "Signature preparer notice for request {SignatureRequestId} has no active recipient.",
                    evt.SignatureRequestId
                );
                return;
            }

            var title = string.IsNullOrWhiteSpace(evt.Title) ? "Untitled request" : evt.Title.Trim();
            var (heading, body) = Copy(evt.Reason, title);
            var link = await sender.StaffLinkAsync(evt.TenantId, $"/signature/requests/{evt.SignatureRequestId:D}", ct);

            await sender.SendAsync(
                new OffboardingNoticeContext(
                    evt.TenantId,
                    evt.EventId,
                    evt.OccurredOn,
                    "signature.preparer_attention_required.v1",
                    NotificationKinds.SignaturePreparerAttentionRequired,
                    NotificationCategory.DocumentsAndSignatures,
                    EmailEventKey,
                    link,
                    evt.SignatureRequestId.ToString(),
                    $"signature.preparer_attention_required:{evt.SignatureRequestId:N}:{evt.Reason}",
                    // Re-ejecutar el retiro re-avisa lo pendiente (p. ej. UnsentRequestKept) con otro EventId.
                    DeliveryKey: $"request:{evt.SignatureRequestId:N}:{evt.Reason}:{evt.OffboardedUserId:N}"
                ),
                [
                    new OffboardingNotice(
                        recipients,
                        heading,
                        body,
                        new Dictionary<string, object?>
                        {
                            ["reason"] = evt.Reason,
                            ["request_title"] = title,
                            ["request_url"] = link,
                        },
                        new Dictionary<string, string> { ["reason"] = evt.Reason, ["status"] = evt.Status }
                    ),
                ],
                ct
            );
        }
    }

    private static async Task<IReadOnlyList<Guid>> ResolveRecipientsAsync(
        SignaturePreparerAttentionRequiredIntegrationEvent evt,
        OffboardingNoticeSender sender,
        CancellationToken ct
    )
    {
        if (evt.RecipientUserId is { } recipient && recipient != Guid.Empty)
        {
            var direct = await sender.ActiveUsersAsync(evt.TenantId, [recipient], ct);
            if (direct.Count > 0)
                return direct;
        }

        // Sin destinatario (o ya inactivo): los admins de firma.
        var permission = string.IsNullOrWhiteSpace(evt.AudiencePermission)
            ? SignaturePermissions.RequestManage
            : evt.AudiencePermission;
        return await sender.ByPermissionsAsync(evt.TenantId, [permission], ct);
    }

    private static (string Title, string Body) Copy(string reason, string title) =>
        reason switch
        {
            SignaturePreparerAttentionReasons.PreparerCleared => (
                $"Choose a new preparer for “{title}”",
                "The preparer of this signature request is no longer with your office, so they were removed from it. "
                    + "Choose a new preparer before you send it."
            ),
            SignaturePreparerAttentionReasons.PreparerChangeBlocked => (
                $"“{title}” needs a new preparer",
                "The preparer of this signature request is no longer with your office and hasn't signed yet. "
                    + "Change the preparer so the return can be signed."
            ),
            SignaturePreparerAttentionReasons.UnsentRequestKept => (
                $"Review the unsent request “{title}”",
                "This signature request was created by someone who's no longer with your office and hasn't been sent. "
                    + "Review it and decide whether to send it or cancel it."
            ),
            _ => (
                $"“{title}” needs your attention",
                "Someone who worked on this signature request is no longer with your office. Open it to see what needs to change."
            ),
        };
}
