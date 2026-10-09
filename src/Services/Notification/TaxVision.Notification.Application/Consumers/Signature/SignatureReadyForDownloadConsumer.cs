using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers.Signature;

/// <summary>
/// Consume <see cref="SignatureReadyForDownloadIntegrationEvent"/> — el documento sellado ya está
/// disponible y con share-link emitido, así que manda el correo de firma completada a cada firmante
/// con el botón "descargar documento firmado". Reemplaza al viejo consumer que salía del evento
/// <c>Completed</c> (que era demasiado temprano: el archivo aún no existía en CloudStorage).
/// </summary>
public static class SignatureReadyForDownloadConsumer
{
    private const string TemplateKey = SignatureTemplateCatalog.CompletedKey;

    private sealed record DownloadItem(string Title, string Link);

    public static async Task Handle(
        SignatureReadyForDownloadIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        ISmsSender smsSender,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hostResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        // P2: la request puede desactivar la entrega del documento firmado a los firmantes.
        if (!evt.SendSealedDocumentToSigners)
            return;

        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            var tenantHost = await hostResolver.ResolveHostAsync(evt.TenantId, ct);

            foreach (var signer in evt.Signers)
            {
                var downloads = evt
                    .SealedFiles.Where(file => !string.IsNullOrEmpty(file.ShareToken))
                    .Select(file => new DownloadItem(
                        file.Title,
                        TenantEmailLinks.PublicShareDownloadLink(
                            tenantHost,
                            portal.Value,
                            file.ShareToken!,
                            signer.Email
                        )
                    ))
                    .ToList();

                // Canal preferido del firmante: SMS con el enlace de descarga si lo eligió y hay teléfono;
                // si no, el correo de siempre. Sin link (share-token vacío) no tiene sentido el SMS → email.
                if (PrefersSms(signer) && downloads.Count > 0)
                {
                    var smsBody = BuildReadySms(signer, downloads, portal.Value);
                    await smsSender.SendAsync(signer.PhoneE164!, smsBody, ct);
                    continue;
                }

                var render = (
                    await scribeClient.RenderAsync(
                        "sig.request_completed.v1",
                        evt.TenantId,
                        new Dictionary<string, object?>
                        {
                            ["full_name"] = signer.FullName,
                            ["completed_at"] = evt.CompletedAtUtc.ToString("yyyy-MM-dd HH:mm"),
                            ["download_links"] = downloads
                                .Select(download => new Dictionary<string, object?>
                                {
                                    ["title"] = download.Title,
                                    ["url"] = download.Link,
                                })
                                .ToList(),
                            ["document_count"] = evt.SealedFiles.Count,
                            ["language"] = signer.Language,
                        },
                        ct
                    )
                ).EnsureRendered("sig.request_completed.v1");

                await gateway.QueueEmailAsync(
                    render.ToDispatchRequest(
                        tenantId: evt.TenantId,
                        to: signer.Email,
                        templateKey: TemplateKey,
                        relatedEventId: evt.EventId,
                        correlationId: correlationId
                    ),
                    ct
                );
            }
        }
    }

    private static bool PrefersSms(SignerContactSnapshot signer) =>
        string.Equals(signer.PreferredChannel, "Sms", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(signer.PhoneE164);

    private static string BuildReadySms(
        SignerContactSnapshot signer,
        IReadOnlyList<DownloadItem> downloads,
        PortalOptions portal
    )
    {
        var product = portal.ProductName;
        var links = string.Join(" ", downloads.Select(download => $"{download.Title}: {download.Link}"));
        return signer.Language == "Es"
            ? $"{product}: {signer.FullName}, tus documentos firmados están listos. {links}"
            : $"{product}: {signer.FullName}, your signed documents are ready. {links}";
    }

    private static string ResolveCorrelationId(SignatureReadyForDownloadIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
