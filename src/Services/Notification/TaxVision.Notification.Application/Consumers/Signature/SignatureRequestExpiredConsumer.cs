using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Consumers.Signature;

/// <summary>
/// Consume <see cref="SignatureRequestExpiredIntegrationEvent"/> — avisa a cada firmante que seguía
/// pendiente que la solicitud venció sin completarse (Fase 8: template previamente "muerto", sin
/// consumer; el usuario confirmó que se usará).
/// </summary>
public static class SignatureRequestExpiredConsumer
{
    private const string TemplateKey = SignatureTemplateCatalog.ExpiredKey;

    public static async Task Handle(
        SignatureRequestExpiredIntegrationEvent evt,
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
            foreach (var signer in evt.PendingSigners)
            {
                var render = (
                    await scribeClient.RenderAsync(
                        "sig.request_expired.v1",
                        evt.TenantId,
                        new Dictionary<string, object?>
                        {
                            ["full_name"] = signer.FullName,
                            ["expired_at"] = evt.ExpiredAtUtc.ToString("yyyy-MM-dd HH:mm"),
                            ["language"] = signer.Language,
                        },
                        ct
                    )
                ).EnsureRendered("sig.request_expired.v1");

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

    private static string ResolveCorrelationId(SignatureRequestExpiredIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
