using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Offboarding;

/// <summary>
/// El preparador de una solicitud enviada pasó a otra persona (retiro o cambio manual). Avisa al nuevo
/// preparador y, si es otra persona, al autor de la solicitud. Quien hizo el cambio a mano no se avisa a sí mismo.
/// </summary>
public static class SignaturePreparerChangedConsumer
{
    public const string EmailEventKey = "sig.preparer_changed.v1";

    public static async Task Handle(
        SignaturePreparerChangedIntegrationEvent evt,
        OffboardingNoticeSender sender,
        ICorrelationContext correlation,
        ILogger<SignaturePreparerChangedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var actor = evt.ChangedByUserId ?? Guid.Empty;
            IReadOnlyList<Guid> preparer =
                evt.NewPreparerUserId == actor
                    ? []
                    : await sender.ActiveUsersAsync(evt.TenantId, [evt.NewPreparerUserId], ct);
            IReadOnlyList<Guid> author =
                evt.CreatedByUserId == evt.NewPreparerUserId || evt.CreatedByUserId == actor
                    ? []
                    : await sender.ActiveUsersAsync(evt.TenantId, [evt.CreatedByUserId], ct);
            if (preparer.Count == 0 && author.Count == 0)
            {
                logger.LogInformation(
                    "Signature preparer change on request {SignatureRequestId} has no one to notify.",
                    evt.SignatureRequestId
                );
                return;
            }

            var title = string.IsNullOrWhiteSpace(evt.Title) ? "Untitled request" : evt.Title.Trim();
            var name = string.IsNullOrWhiteSpace(evt.NewPreparerDisplayName)
                ? "A teammate"
                : evt.NewPreparerDisplayName.Trim();
            var offboarding = evt.Reason == SignaturePreparerChangeReasons.Offboarding;
            var link = await sender.StaffLinkAsync(evt.TenantId, $"/signature/requests/{evt.SignatureRequestId:D}", ct);

            await sender.SendAsync(
                new OffboardingNoticeContext(
                    evt.TenantId,
                    evt.EventId,
                    evt.OccurredOn,
                    "signature.preparer_changed.v1",
                    NotificationKinds.SignaturePreparerChanged,
                    NotificationCategory.DocumentsAndSignatures,
                    EmailEventKey,
                    link,
                    evt.SignatureRequestId.ToString(),
                    // Un cambio posterior a otro preparador abre una fila nueva.
                    $"signature.preparer_changed:{evt.SignatureRequestId:N}:{evt.NewPreparerUserId:N}"
                ),
                [
                    Notice(preparer, isNewPreparer: true, offboarding, title, name, link),
                    Notice(author, isNewPreparer: false, offboarding, title, name, link),
                ],
                ct
            );
        }
    }

    private static OffboardingNotice Notice(
        IReadOnlyList<Guid> recipients,
        bool isNewPreparer,
        bool offboarding,
        string title,
        string name,
        string link
    )
    {
        var why = offboarding ? "The previous preparer is no longer with your office. " : string.Empty;
        var (heading, body) = isNewPreparer
            ? (
                $"You're now the preparer for “{title}”",
                why + "You'll sign this return as its preparer. Open the request to review it."
            )
            : ($"New preparer for “{title}”", why + $"{name} is now the preparer of this signature request.");
        return new OffboardingNotice(
            recipients,
            heading,
            body,
            new Dictionary<string, object?>
            {
                ["is_new_preparer"] = isNewPreparer,
                ["is_offboarding"] = offboarding,
                ["request_title"] = title,
                ["preparer_name"] = name,
                ["request_url"] = link,
            },
            new Dictionary<string, string> { ["preparerName"] = name }
        );
    }
}
