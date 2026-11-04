using BuildingBlocks.Results;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.Abstractions.Payments;

/// <summary>
/// Contrato canónico que todo payment provider implementa para PaymentClient. Diferencia
/// deliberada frente a <c>TaxVision.PaymentApp</c>: allá las credenciales viven en
/// <c>IOptions&lt;StripeOptions&gt;</c> inyectado una vez (una sola cuenta Stripe, la de la
/// plataforma); acá cada tenant tiene su propia cuenta, así que el mismo adapter singleton
/// atiende a todos los tenants y las credenciales YA descifradas
/// (<see cref="TenantProviderCredentials"/>) viajan por parámetro en cada llamada. El handler
/// las obtiene de <c>TenantPaymentConfig</c> vía <c>ISecretProtector</c> justo antes de invocar
/// al adapter y nunca las persiste ni las loguea.
///
/// Ningún método lanza excepción para errores esperados del provider — todo se envuelve en
/// <see cref="Result{T}"/>.
/// </summary>
public interface IPaymentProvider
{
    PaymentProviderCode Code { get; }

    /// <summary>Qué credenciales pide este proveedor (catálogo del CRM y reglas de activación).</summary>
    PaymentProviderDescriptor Descriptor { get; }

    Task<Result<ChargeAuthorizationResult>> AuthorizeChargeAsync(
        TenantProviderCredentials credentials,
        ChargeAuthorizationRequest request,
        CancellationToken ct
    );

    Task<Result<RefundResult>> RefundAsync(
        TenantProviderCredentials credentials,
        string providerChargeReference,
        Money amount,
        string reason,
        CancellationToken ct
    );

    /// <summary>Verifica que el webhook venga del proveedor. Cada adaptador lee los encabezados que usa
    /// (Stripe: <c>Stripe-Signature</c>; PayPal: los <c>PAYPAL-*</c>) y las credenciales de la oficina
    /// (<see cref="TenantProviderCredentials.WebhookSecret"/> y, si verifica contra su API, las claves).
    /// <paramref name="headers"/> no distingue mayúsculas.</summary>
    Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
        string rawPayload,
        IReadOnlyDictionary<string, string> headers,
        TenantProviderCredentials credentials,
        CancellationToken ct
    );

    /// <summary>Traduce un webhook YA verificado (<see cref="VerifyWebhookSignatureAsync"/>) a
    /// datos canónicos que <c>TenantPayment</c> puede aplicar. Solo el adapter conoce el
    /// formato del payload — Application nunca deserializa JSON de provider.</summary>
    Task<Result<WebhookEventPayload>> ParseWebhookEventAsync(string rawPayload, string eventType, CancellationToken ct);

    /// <summary>Estado actual del cargo según el proveedor, para resolver pagos atascados sin webhook.
    /// Devuelve el mismo payload canónico que el webhook para aplicarlo por el mismo camino; un cargo
    /// que el proveedor aún no resolvió vuelve como <c>Processing</c>/<c>RequiresAction</c>.</summary>
    Task<Result<WebhookEventPayload>> GetChargeStatusAsync(
        TenantProviderCredentials credentials,
        ChargeStatusLookup lookup,
        CancellationToken ct
    );
}
