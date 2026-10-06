using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers.Signature;

/// <summary>
/// F7 — consume <see cref="SignerPartialCopyReadyIntegrationEvent"/> y manda al firmante el correo
/// con su copia parcial. El share-token se convierte a URL pública del portal del tenant; si viene
/// vacío el template oculta el botón y el preparador puede reenviar la copia después.
/// </summary>
public static class SignerPartialCopyReadyConsumer
{
    public static async Task Handle(
        SignerPartialCopyReadyIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hostResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            var tenantHost = await hostResolver.ResolveHostAsync(evt.TenantId, ct);
            var downloadLink = string.IsNullOrEmpty(evt.ShareToken)
                ? null
                : TenantEmailLinks.PublicShareDownloadLink(tenantHost, portal.Value, evt.ShareToken, evt.SignerEmail);

            var render = (
                await scribeClient.RenderAsync(
                    "sig.partial_copy_ready.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["full_name"] = evt.SignerFullName,
                        ["document_title"] = evt.DocumentTitle,
                        ["signed_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
                        ["download_link"] = downloadLink,
                        ["language"] = evt.Language,
                        // F7 — permite al template adaptar copy cuando el request tiene un único firmante.
                        ["total_signers"] = evt.TotalSignersCount,
                        // F7 — si el preparador no manda el sealed, el template no debe prometerlo.
                        ["send_sealed_to_signers"] = evt.SendSealedToSigners,
                    },
                    ct
                )
            ).EnsureRendered(SignatureTemplateCatalog.PartialCopyKey);

            await gateway.QueueEmailAsync(
                render.ToDispatchRequest(
                    tenantId: evt.TenantId,
                    to: evt.SignerEmail,
                    templateKey: SignatureTemplateCatalog.PartialCopyKey,
                    relatedEventId: evt.EventId,
                    correlationId: correlationId
                ),
                ct
            );
        }
    }

    private static string ResolveCorrelationId(SignerPartialCopyReadyIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? Guid.NewGuid().ToString("N") : evt.CorrelationId;
}
