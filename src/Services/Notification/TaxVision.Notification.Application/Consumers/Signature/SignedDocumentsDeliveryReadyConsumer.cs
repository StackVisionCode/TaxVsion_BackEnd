using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Platform;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers.Signature;

/// <summary>
/// Consume <see cref="SignedDocumentsDeliveryReadyIntegrationEvent"/>: UN correo (o SMS, si el firmante lo
/// prefiere) por entrega, con todos sus documentos firmados y certificados — aunque la solicitud tenga
/// varios. Los enlaces van a la página de entrega de Signature (<c>/d/{token}</c>), no a un share; el token
/// llega cifrado y el correo sale confidencial. Si el firmante es cliente con portal, se le indica también.
/// </summary>
public static class SignedDocumentsDeliveryReadyConsumer
{
    private const string PartialCopyPurpose = "PartialCopy";

    public static async Task Handle(
        SignedDocumentsDeliveryReadyIntegrationEvent evt,
        SignedDeliveryLinkResolver deliveryLinks,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        ISmsSender smsSender,
        IPlatformSettings platform,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;
        using (correlation.Push(correlationId))
        {
            if (await deliveryLinks.ResolveAsync(evt, ct) is not { } target)
                return;

            var signer = evt.Signer;
            var pageUrl = target.PageUrl;
            var isPartialCopy = string.Equals(evt.Purpose, PartialCopyPurpose, StringComparison.OrdinalIgnoreCase);

            if (target.PrefersSms)
            {
                await smsSender.SendAsync(
                    signer.PhoneE164!,
                    BuildSms(signer, pageUrl, isPartialCopy, platform.Current.BrandName),
                    ct,
                    templateKey: null,
                    idempotencyToken: $"{evt.EventId:N}:{signer.SignerId:N}"
                );
                return;
            }

            var portalUrl = await deliveryLinks.PortalUrlAsync(evt, target, ct);
            var links = evt
                .Items.Select(item => new Dictionary<string, object?>
                {
                    ["title"] = item.Title,
                    ["url"] = deliveryLinks.ItemUrl(target, item.ItemId),
                    ["kind"] = item.Kind,
                })
                .ToList();
            var variables = new Dictionary<string, object?>
            {
                ["full_name"] = signer.FullName,
                ["download_links"] = links,
                ["document_count"] = evt.Items.Count,
                ["language"] = signer.Language,
                ["delivery_url"] = pageUrl,
                ["expires_at"] = evt.ExpiresAtUtc.ToString("yyyy-MM-dd"),
                ["portal_url"] = portalUrl,
                ["is_reissue"] = evt.IsReissue,
                ["can_request_new_link"] = evt.CanRequestNewLink,
                ["has_attachments"] = evt.AttachmentFileIds.Count > 0,
            };

            string eventKey;
            string templateKey;
            if (isPartialCopy)
            {
                eventKey = "sig.partial_copy_ready.v1";
                templateKey = SignatureTemplateCatalog.PartialCopyKey;
                variables["signed_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
                variables["total_signers"] = evt.TotalSignersCount;
                variables["send_sealed_to_signers"] = evt.SendSealedToSigners;
            }
            else
            {
                eventKey = "sig.request_completed.v1";
                templateKey = SignatureTemplateCatalog.CompletedKey;
                variables["completed_at"] = evt.CompletedAtUtc.ToString("yyyy-MM-dd HH:mm");
            }

            var render = (
                await scribeClient.RenderAsync(eventKey, evt.TenantId, variables, ScribeRenderAudience.Client, ct)
            ).EnsureRendered(eventKey);

            await gateway.QueueEmailAsync(
                render.ToDispatchRequest(
                    tenantId: evt.TenantId,
                    to: signer.Email,
                    templateKey: templateKey,
                    relatedEventId: evt.EventId,
                    correlationId: correlationId
                ) with
                {
                    // El cuerpo lleva un enlace con secreto: cola privada de Postmaster, no el fanout.
                    Confidential = true,
                    AttachmentFileIds = evt.AttachmentFileIds.Count > 0 ? evt.AttachmentFileIds : null,
                },
                ct
            );
        }
    }

    private static string BuildSms(SignerContactSnapshot signer, string pageUrl, bool isPartialCopy, string product) =>
        (signer.Language, isPartialCopy) switch
        {
            ("Es", true) => $"{product}: {signer.FullName}, tu copia firmada está lista: {pageUrl}",
            ("Es", false) => $"{product}: {signer.FullName}, tus documentos firmados están listos: {pageUrl}",
            (_, true) => $"{product}: {signer.FullName}, your signed copy is ready: {pageUrl}",
            _ => $"{product}: {signer.FullName}, your signed documents are ready: {pageUrl}",
        };
}
