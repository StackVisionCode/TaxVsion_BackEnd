using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Consumers.Signature;

/// <summary>
/// Consume <see cref="SignerRejectedIntegrationEvent"/> — avisa a los demás firmantes que todavía
/// tenían la solicitud pendiente que fue cancelada porque otro firmante la rechazó (Fase 8: template
/// previamente "muerto", sin consumer; el usuario confirmó que se usará). Al firmante que rechazó no
/// se le notifica — ya sabe que rechazó.
/// </summary>
public static class SignerRejectedConsumer
{
    private const string TemplateKey = SignatureTemplateCatalog.DeclinedKey;

    public static async Task Handle(
        SignerRejectedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            // Hardening Fase 7: antes, un render fallido se logueaba y el loop hacía "continue" —
            // ese firmante pendiente se quedaba sin notificar para siempre (Wolverine veía el
            // handler como exitoso). EnsureRendered lanza ScribeRenderFailedException en el primer
            // fallo para que el mensaje completo se reintente.
            // Se renderiza todo antes de escribir: Scribe no corre con filas ya bloqueadas (NOT-07).
            var rendered = new List<(string Email, ScribeRenderedEmail Render)>();
            foreach (var signer in evt.PendingSigners)
            {
                var render = (
                    await scribeClient.RenderAsync(
                        "sig.signer_rejected.v1",
                        evt.TenantId,
                        new Dictionary<string, object?>
                        {
                            ["full_name"] = signer.FullName,
                            ["language"] = signer.Language,
                        },
                        ScribeRenderAudience.Client,
                        ct
                    )
                ).EnsureRendered("sig.signer_rejected.v1");

                rendered.Add((signer.Email, render));
            }

            foreach (var (email, render) in rendered)
                await gateway.QueueEmailAsync(
                    render.ToDispatchRequest(
                        tenantId: evt.TenantId,
                        to: email,
                        templateKey: TemplateKey,
                        relatedEventId: evt.EventId,
                        correlationId: correlationId
                    ),
                    ct
                );
        }
    }

    private static string ResolveCorrelationId(SignerRejectedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
