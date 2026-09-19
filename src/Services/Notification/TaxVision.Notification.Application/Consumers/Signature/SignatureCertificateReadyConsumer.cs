using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers.Signature;

/// <summary>
/// Consume <see cref="SignatureCertificateReadyForDownloadIntegrationEvent"/> — el Certificate of
/// Completion ya está disponible y con share-link, así que lo entrega a cada firmante por su canal:
/// SMS si lo prefiere y tiene teléfono, si no email renderizado en Scribe (misma vía que el resto de
/// correos de Signature). Un email por firmante.
/// </summary>
public static class SignatureCertificateReadyConsumer
{
    private const string TemplateKey = SignatureTemplateCatalog.CertificateKey;

    public static async Task Handle(
        SignatureCertificateReadyForDownloadIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        ISmsSender smsSender,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hostResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var tenantHost = await hostResolver.ResolveHostAsync(evt.TenantId, ct);

            foreach (var signer in evt.Signers)
            {
                var downloadLink = string.IsNullOrEmpty(evt.ShareToken)
                    ? null
                    : TenantEmailLinks.PublicShareDownloadLink(tenantHost, portal.Value, evt.ShareToken, signer.Email);

                var prefersSms =
                    string.Equals(signer.PreferredChannel, "Sms", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(signer.PhoneE164);

                if (prefersSms && !string.IsNullOrEmpty(downloadLink))
                {
                    await smsSender.SendAsync(signer.PhoneE164!, BuildSms(signer, portal.Value, downloadLink), ct);
                    continue;
                }

                var render = (
                    await scribeClient.RenderAsync(
                        "sig.certificate_ready.v1",
                        evt.TenantId,
                        new Dictionary<string, object?>
                        {
                            ["full_name"] = signer.FullName,
                            ["completed_at"] = evt.CompletedAtUtc.ToString("yyyy-MM-dd HH:mm"),
                            ["download_link"] = downloadLink,
                            ["language"] = signer.Language,
                        },
                        ct
                    )
                ).EnsureRendered("sig.certificate_ready.v1");

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

    private static string BuildSms(SignerContactSnapshot signer, PortalOptions portal, string downloadLink) =>
        signer.Language == "Es"
            ? $"{portal.ProductName}: {signer.FullName}, tu certificado de firma está listo. Descárgalo: {downloadLink}"
            : $"{portal.ProductName}: {signer.FullName}, your signature certificate is ready. Download it: {downloadLink}";
}
