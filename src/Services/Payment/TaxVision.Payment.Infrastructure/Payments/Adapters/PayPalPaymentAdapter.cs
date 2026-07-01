using TaxVision.Payment.Application.Abstractions;

namespace TaxVision.Payment.Infrastructure.Payments.Adapters;

/// <summary>
/// PayPal adapter stub. The PayPal SDK integration is pending.
/// Returns a structured failure result indicating the provider is not yet fully configured.
/// Replace the body of each method with the actual PayPal REST API calls when the PayPal SDK is added.
/// </summary>
public sealed class PayPalPaymentAdapter : IPaymentAdapter
{
    private const string NotImplementedReason =
        "PayPal payment processing is not yet fully implemented. Please configure a different payment provider.";

    public Task<TenantPaymentResult> ChargeAsync(
        string secretKey,
        long amountCents,
        string currency,
        string description,
        CancellationToken ct = default)
    {
        return Task.FromResult(new TenantPaymentResult(false, null, NotImplementedReason));
    }

    public Task<TenantPaymentResult> RefundAsync(
        string secretKey,
        string externalTransactionId,
        CancellationToken ct = default)
    {
        return Task.FromResult(new TenantPaymentResult(false, null, NotImplementedReason));
    }
}
