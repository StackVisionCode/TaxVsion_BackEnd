using System.Security.Cryptography;
using System.Text.Json;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Directory.Abstractions;

namespace TaxVision.Notification.Application.Consumers.Signature;

/// <summary>Enlaces de una entrega firmada, ya abiertos y con la oficina resuelta.</summary>
public sealed record SignedDeliveryLinks(string Token, string? TenantHost, bool PrefersSms, string PageUrl);

/// <summary>
/// Decide si la entrega sale y hacia dónde: abre el token, aplica la política del cliente y arma los
/// enlaces de la página de entrega y del portal.
/// </summary>
public sealed class SignedDeliveryLinkResolver(
    IProtectedPayloadCodec codec,
    ICustomerContactPolicyRepository contactPolicies,
    IRecipientResolver recipientResolver,
    IOptions<PortalOptions> portal,
    ITenantHostResolver hostResolver,
    ILogger<SignedDeliveryLinkResolver> logger
)
{
    /// <summary>Null si no hay que enviar: token inválido o canal apagado por el cliente.</summary>
    public async Task<SignedDeliveryLinks?> ResolveAsync(
        SignedDocumentsDeliveryReadyIntegrationEvent evt,
        CancellationToken ct
    )
    {
        string token;
        try
        {
            token = codec
                .Unprotect<SignatureDeliveryLinkPayload>(
                    evt.ProtectedToken,
                    SignatureDeliveryPayloadSchemas.DeliveryLink,
                    SignatureDeliveryPayloadSchemas.BindingFor(evt.TenantId, evt.DeliveryId)
                )
                .Token;
        }
        // Solo lo permanente se descarta; un fallo transitorio del anillo de claves sube y Wolverine reintenta.
        catch (Exception ex)
            when (ex is CryptographicException or FormatException or JsonException or ArgumentException)
        {
            // Payload adulterado o de otra entrega: reintentar no lo arregla.
            logger.LogError(ex, "Delivery {DeliveryId}: the link token could not be decrypted.", evt.DeliveryId);
            return null;
        }

        var signer = evt.Signer;
        var prefersSms =
            string.Equals(signer.PreferredChannel, "Sms", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(signer.PhoneE164);

        // T7.3: un firmante que es cliente con ContactPolicy decide su canal; los externos no se filtran.
        if (
            !await CustomerContactPolicyFilter.AllowsForOptionalCustomerAsync(
                evt.TenantId,
                signer.MappedCustomerId,
                prefersSms ? CustomerContactChannel.Sms : CustomerContactChannel.Email,
                contactPolicies,
                ct
            )
        )
            return null;

        var tenantHost = await hostResolver.ResolveHostAsync(evt.TenantId, ct);
        return new SignedDeliveryLinks(
            token,
            tenantHost,
            prefersSms,
            TenantEmailLinks.SignatureDeliveryPage(tenantHost, portal.Value, token)
        );
    }

    public string ItemUrl(SignedDeliveryLinks links, Guid itemId) =>
        TenantEmailLinks.SignatureDeliveryPage(links.TenantHost, portal.Value, links.Token, itemId);

    /// <summary>Solo si los archivos quedaron en su carpeta y el cliente tiene una cuenta de portal activa.</summary>
    public async Task<string?> PortalUrlAsync(
        SignedDocumentsDeliveryReadyIntegrationEvent evt,
        SignedDeliveryLinks links,
        CancellationToken ct
    )
    {
        if (!evt.AvailableInClientPortal || evt.Signer.MappedCustomerId is not { } customerId)
            return null;
        var portalUser = await CustomerPortalRecipients.ResolveActivePortalUserAsync(
            evt.TenantId,
            customerId,
            recipientResolver,
            ct
        );
        return portalUser is null ? null : TenantEmailLinks.ClientDocuments(links.TenantHost, portal.Value);
    }
}
