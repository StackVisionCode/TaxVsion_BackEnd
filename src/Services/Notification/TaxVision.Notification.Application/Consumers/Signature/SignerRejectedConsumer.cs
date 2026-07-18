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
            // fallo para que el mensaje completo se reintente; los firmantes ya notificados en el
            // intento fallido pueden recibir el email duplicado en el reintento — trade-off aceptado
            // de at-least-once, igual que el resto del monorepo (ver IdempotencyReservationInProgressException
            // de Postmaster para el mismo patrón).
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
                        ct
                    )
                ).EnsureRendered("sig.signer_rejected.v1");

                await gateway.QueueEmailAsync(
                    new EmailDispatchRequest(
                        TenantId: evt.TenantId,
                        To: signer.Email,
                        Subject: render.Subject,
                        HtmlBody: render.Html,
                        TextBody: render.Text ?? string.Empty,
                        TemplateKey: TemplateKey,
                        RelatedEventId: evt.EventId,
                        CorrelationId: correlationId,
                        InlineAssets: render.InlineAssets
                    ),
                    ct
                );
            }
        }
    }

    private static string ResolveCorrelationId(SignerRejectedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
