using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// Emaila la página pública branded de un share-link a un destinatario externo (visibility
/// ExternalRecipients). Un evento por destinatario; el link lleva el <c>?email</c> que el backend
/// valida. Mismo patrón que SignerInvitedConsumer: render en Scribe + despacho por el gateway,
/// idempotente por RelatedEventId (EventId único por destinatario).
/// </summary>
public static class ShareLinkExternalRecipientInvitedConsumer
{
    private const string TemplateKey = "storage.share_invited.v1";

    public static async Task Handle(
        ShareLinkExternalRecipientInvitedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hostResolver,
        ICorrelationContext correlation,
        ILogger<ShareLinkExternalRecipientInvitedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;
        using (correlation.Push(correlationId))
        {
            // El destinatario abre la página branded en el subdominio de la oficina.
            var tenantHost = await hostResolver.ResolveHostAsync(evt.TenantId, ct);
            var openLink = TenantEmailLinks.PublicSharePageLink(tenantHost, portal.Value, evt.PlainToken, evt.Email);

            var render = (
                await scribeClient.RenderAsync(
                    TemplateKey,
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["file_name"] = evt.FileName,
                        ["open_link"] = openLink,
                        ["expires_at"] = ScribeDates.Instant(evt.ExpiresAtUtc),
                        ["permission"] = evt.Permission,
                        ["language"] = evt.Language,
                    },
                    ScribeRenderAudience.Client,
                    ScribeReader.From(evt.Language),
                    ct
                )
            ).EnsureRendered(TemplateKey);

            var result = await gateway.QueueEmailAsync(
                render.ToDispatchRequest(
                    tenantId: evt.TenantId,
                    to: evt.Email,
                    templateKey: TemplateKey,
                    relatedEventId: evt.EventId,
                    correlationId: correlationId
                ),
                ct
            );

            if (!result.IsSuccess)
                logger.LogWarning(
                    "ShareLinkExternalRecipientInvited email failed for share {ShareLinkId}: {Error}",
                    evt.ShareLinkId,
                    result.Error
                );
        }
    }
}
