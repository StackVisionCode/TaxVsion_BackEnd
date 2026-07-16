using BuildingBlocks.Results;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.Abstractions.Payments;

/// <summary>
/// Contrato canónico que TODO payment provider implementa — Stripe, Intellipay, Manual, y
/// cualquier provider futuro ("absolutamente cualquier sistema de pago"). El Domain nunca
/// implementa ni referencia esta interfaz ni ningún SDK de provider (guardrail §44.1 ley 1);
/// solo Application (este contrato) e Infrastructure (los adapters) lo conocen.
///
/// Ningún método lanza excepción para errores esperados del provider — todo se envuelve en
/// <see cref="Result{T}"/>. Cero conocimiento cruzado entre adapters: agregar un provider
/// nuevo nunca modifica un adapter existente (guardrail §44.1 ley 3).
/// </summary>
public interface IPaymentProvider
{
    PaymentProviderCode Code { get; }

    ProviderCapabilities Capabilities { get; }

    Task<Result<ProviderCustomerToken>> GetOrCreateCustomerAsync(
        Guid tenantId, string email, string? name, CancellationToken ct);

    Task<Result<ChargeAuthorizationResult>> AuthorizeChargeAsync(
        ChargeAuthorizationRequest request, CancellationToken ct);

    Task<Result<CaptureResult>> CaptureAsync(
        string providerChargeReference, Money amount, CancellationToken ct);

    Task<Result<RefundResult>> RefundAsync(
        string providerChargeReference, Money amount, string reason, CancellationToken ct);

    Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
        string rawPayload, string signatureHeader, string webhookSecret, CancellationToken ct);

    /// <summary>Traduce un webhook YA verificado (<see cref="VerifyWebhookSignatureAsync"/>)
    /// a datos canónicos que <c>SaaSPayment</c> puede aplicar. Solo el adapter conoce el
    /// formato del payload — Application nunca deserializa JSON de provider.</summary>
    Task<Result<WebhookEventPayload>> ParseWebhookEventAsync(string rawPayload, string eventType, CancellationToken ct);

    /// <summary>Confirmación out-of-band del estado real de un cargo, consultada
    /// directamente al provider (no vía webhook). La usa
    /// <c>PendingChargeReconciliationJob</c> para resolver pagos atascados en Processing
    /// tras una caída a mitad de cobro (§1714 del diseño).</summary>
    Task<Result<ChargeAuthorizationResult>> GetChargeStatusAsync(string providerChargeReference, CancellationToken ct);

    /// <summary>Adjunta un método ya tokenizado por el provider en el cliente (p.ej. Stripe
    /// Elements) al customer, y devuelve la metadata autoritativa (brand/last4/expiración)
    /// directamente del provider — el backend nunca confía en lo que el frontend afirma
    /// sobre una tarjeta.</summary>
    Task<Result<SavedPaymentMethodInfo>> AttachPaymentMethodAsync(
        ProviderCustomerToken customer, string paymentMethodReference, CancellationToken ct);

    /// <summary>Desvincula el método del customer en el provider — el registro local queda
    /// marcado detached independientemente de esto, pero sin esta llamada el método seguiría
    /// chargeable directo desde el dashboard del provider.</summary>
    Task<Result> DetachPaymentMethodAsync(string paymentMethodReference, CancellationToken ct);
}
