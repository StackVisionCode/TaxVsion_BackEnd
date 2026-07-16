using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Infrastructure.Providers.Manual;

/// <summary>
/// Fallback para cobros que se liquidan fuera de banda (transferencia bancaria, cheque,
/// efectivo) y que un PlatformAdmin confirma manualmente. No hay gateway externo: el
/// "cobro" siempre se autoriza de inmediato — la evidencia real de que el dinero llegó vive
/// en el proceso operativo (referencia bancaria que el admin adjunta como
/// <see cref="ChargeAuthorizationRequest.Metadata"/>), no en una llamada HTTP.
/// </summary>
[PaymentProvider(PaymentProviderCode.Manual)]
public sealed class ManualPaymentAdapter(ILogger<ManualPaymentAdapter> logger) : IPaymentProvider
{
    public PaymentProviderCode Code => PaymentProviderCode.Manual;
    public ProviderCapabilities Capabilities => ManualCapabilities.Instance;

    public Task<Result<ProviderCustomerToken>> GetOrCreateCustomerAsync(
        Guid tenantId, string email, string? name, CancellationToken ct) =>
        Task.FromResult(Result.Success(new ProviderCustomerToken(tenantId.ToString("N"), PaymentProviderCode.Manual)));

    public Task<Result<ChargeAuthorizationResult>> AuthorizeChargeAsync(ChargeAuthorizationRequest request, CancellationToken ct)
    {
        var reference = $"manual_{Guid.NewGuid():N}";
        logger.LogInformation(
            "Manual charge recorded as succeeded. Reference={Reference} IdempotencyKey={IdempotencyKey}",
            reference, request.IdempotencyKey.Value);

        return Task.FromResult(Result.Success(new ChargeAuthorizationResult(
            ProviderChargeReference: reference,
            Status: PaymentStatus.Succeeded)));
    }

    public Task<Result<CaptureResult>> CaptureAsync(string providerChargeReference, Money amount, CancellationToken ct) =>
        Task.FromResult(Result.Success(new CaptureResult(providerChargeReference, PaymentStatus.Succeeded, amount)));

    public Task<Result<RefundResult>> RefundAsync(string providerChargeReference, Money amount, string reason, CancellationToken ct)
    {
        var reference = $"manual_refund_{Guid.NewGuid():N}";
        logger.LogInformation(
            "Manual refund recorded. Reference={Reference} OriginalCharge={OriginalCharge} Reason={Reason}",
            reference, providerChargeReference, reason);

        return Task.FromResult(Result.Success(new RefundResult(reference, PaymentStatus.Refunded, amount)));
    }

    public Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
        string rawPayload, string signatureHeader, string webhookSecret, CancellationToken ct) =>
        Task.FromResult(Result.Failure<WebhookVerificationResult>(
            new Error("Manual.WebhookSignature.NotSupported", "The Manual provider has no webhooks — payments are confirmed by an admin action.")));

    public Task<Result<WebhookEventPayload>> ParseWebhookEventAsync(string rawPayload, string eventType, CancellationToken ct) =>
        Task.FromResult(Result.Failure<WebhookEventPayload>(
            new Error("Manual.Webhook.NotSupported", "The Manual provider has no webhooks.")));

    /// <summary>Manual siempre autoriza de inmediato — nunca queda en Processing, así que
    /// no hay nada que reconciliar. Devuelve Succeeded como confirmación trivial.</summary>
    public Task<Result<ChargeAuthorizationResult>> GetChargeStatusAsync(string providerChargeReference, CancellationToken ct) =>
        Task.FromResult(Result.Success(new ChargeAuthorizationResult(providerChargeReference, PaymentStatus.Succeeded)));

    public Task<Result<SavedPaymentMethodInfo>> AttachPaymentMethodAsync(
        ProviderCustomerToken customer, string paymentMethodReference, CancellationToken ct) =>
        Task.FromResult(Result.Failure<SavedPaymentMethodInfo>(
            new Error("Manual.PaymentMethod.NotSupported", "The Manual provider has no tokenized payment methods.")));

    public Task<Result> DetachPaymentMethodAsync(string paymentMethodReference, CancellationToken ct) =>
        Task.FromResult(Result.Failure(new Error("Manual.PaymentMethod.NotSupported", "The Manual provider has no tokenized payment methods.")));
}
