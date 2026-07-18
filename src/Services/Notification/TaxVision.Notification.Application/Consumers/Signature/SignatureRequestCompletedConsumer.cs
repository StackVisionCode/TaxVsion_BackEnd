using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Consumers.Signature;

/// <summary>
/// Consume <see cref="SignatureRequestCompletedIntegrationEvent"/> — envía una confirmación por
/// email a cada firmante que participó (Fase 8: template previamente "muerto", sin consumer; el
/// usuario confirmó que se usará). El evento trae un snapshot de contacto por firmante
/// (<see cref="SignerContactSnapshot"/>) desde Signature, así que no hace falta ningún lookup.
/// </summary>
public static class SignatureRequestCompletedConsumer
{
    private const string TemplateKey = SignatureTemplateCatalog.CompletedKey;

    public static async Task Handle(
        SignatureRequestCompletedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            // Hardening Fase 7: ver el mismo comentario en SignerRejectedConsumer — EnsureRendered
            // reemplaza el log+continue que dejaba firmantes sin notificar en silencio.
            foreach (var signer in evt.Signers)
            {
                var render = (
                    await scribeClient.RenderAsync(
                        "sig.request_completed.v1",
                        evt.TenantId,
                        new Dictionary<string, object?>
                        {
                            ["full_name"] = signer.FullName,
                            ["completed_at"] = evt.CompletedAtUtc.ToString("yyyy-MM-dd HH:mm"),
                            ["language"] = signer.Language,
                        },
                        ct
                    )
                ).EnsureRendered("sig.request_completed.v1");

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

    private static string ResolveCorrelationId(SignatureRequestCompletedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
