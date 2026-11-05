using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Providers.PayPal;

namespace TaxVision.PaymentApp.Tests.TestDoubles;

/// <summary>Provider PayPal falso que solo responde el estado de un cobro; el resto lanza.
/// <paramref name="onStatusRequested"/> corre antes de responder (p. ej. para simular un webhook en paralelo).</summary>
public sealed class StatusPaymentProvider(
    Result<ChargeAuthorizationResult> result,
    Func<string, Task>? onStatusRequested = null
) : IPaymentProvider
{
    /// <summary>Cuántas veces se consultó el estado del cobro (GetChargeStatus o FinalizeHostedCheckout).</summary>
    public int StatusCalls { get; private set; }

    public PaymentProviderCode Code => PaymentProviderCode.PayPal;
    public ProviderCapabilities Capabilities => PayPalCapabilities.Instance;

    public Task<Result<ChargeAuthorizationResult>> GetChargeStatusAsync(
        string providerChargeReference,
        CancellationToken ct
    ) => ReplyAsync(providerChargeReference);

    public Task<Result<ChargeAuthorizationResult>> FinalizeHostedCheckoutAsync(
        string providerChargeReference,
        Money amount,
        CancellationToken ct
    ) => ReplyAsync(providerChargeReference);

    private async Task<Result<ChargeAuthorizationResult>> ReplyAsync(string providerChargeReference)
    {
        StatusCalls++;
        if (onStatusRequested is not null)
            await onStatusRequested(providerChargeReference);
        return result;
    }

    public Task<Result<ProviderCustomerToken>> GetOrCreateCustomerAsync(
        Guid tenantId,
        string email,
        string? name,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result<ChargeAuthorizationResult>> AuthorizeChargeAsync(
        ChargeAuthorizationRequest request,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result<CaptureResult>> CaptureAsync(
        string providerChargeReference,
        Money amount,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result<RefundResult>> RefundAsync(
        string providerChargeReference,
        Money amount,
        string reason,
        string idempotencyKey,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
        ProviderWebhookVerificationRequest request,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result<WebhookEventPayload>> ParseWebhookEventAsync(
        string rawPayload,
        string eventType,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result<SetupIntentInfo>> CreateSetupIntentAsync(ProviderCustomerToken customer, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<Result<SavedPaymentMethodInfo>> AttachPaymentMethodAsync(
        ProviderCustomerToken customer,
        string paymentMethodReference,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result> DetachPaymentMethodAsync(string paymentMethodReference, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<Result<HostedCheckoutSessionResult>> CreateHostedCheckoutSessionAsync(
        HostedCheckoutSessionRequest request,
        CancellationToken ct
    ) => throw new NotSupportedException();
}
