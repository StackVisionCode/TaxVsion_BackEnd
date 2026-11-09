using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Directory.Abstractions;

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
        ICustomerContactPolicyRepository contactPolicies,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            // Se renderiza todo antes de escribir: Scribe no corre con filas ya bloqueadas (NOT-07).
            var rendered = new List<(string Email, ScribeRenderedEmail Render)>();
            // Hardening Fase 7: ver el mismo comentario en SignerRejectedConsumer — EnsureRendered
            // reemplaza el log+continue que dejaba firmantes sin notificar en silencio.
            foreach (var signer in evt.PendingSigners)
            {
                // T7.3: cliente del tenant con ContactPolicy.Email = false → se omite.
                // Externos sin MappedCustomerId siguen recibiendo (es su único canal).
                if (
                    !await CustomerContactPolicyFilter.AllowsForOptionalCustomerAsync(
                        evt.TenantId,
                        signer.MappedCustomerId,
                        CustomerContactChannel.Email,
                        contactPolicies,
                        ct
                    )
                )
                    continue;

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
                        ScribeRenderAudience.Client,
                        ct
                    )
                ).EnsureRendered("sig.request_expired.v1");

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

    private static string ResolveCorrelationId(SignatureRequestExpiredIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
