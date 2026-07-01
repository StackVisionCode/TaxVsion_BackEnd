using Stripe;
using TaxVision.Payment.Application.Abstractions;

namespace TaxVision.Payment.Infrastructure.Payments.Adapters;

/// <summary>
/// Stripe adapter for tenant-side payments using the tenant's own Stripe account.
/// The secretKey passed here is the tenant's own Stripe secret key (decrypted by the caller).
/// </summary>
public sealed class StripePaymentAdapter : IPaymentAdapter
{
    public async Task<TenantPaymentResult> ChargeAsync(
        string secretKey,
        long amountCents,
        string currency,
        string description,
        CancellationToken ct = default)
    {
        try
        {
            var client = new StripeClient(secretKey);
            var intentService = new PaymentIntentService(client);

            var options = new PaymentIntentCreateOptions
            {
                Amount = amountCents,
                Currency = currency.ToLowerInvariant(),
                Description = description,
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true,
                    AllowRedirects = "never"
                },
                Confirm = false
            };
            var intent = await intentService.CreateAsync(options, cancellationToken: ct);
            return new TenantPaymentResult(true, intent.Id, null);
        }
        catch (StripeException ex)
        {
            return new TenantPaymentResult(false, null, ex.StripeError?.Message ?? ex.Message);
        }
    }

    public async Task<TenantPaymentResult> RefundAsync(
        string secretKey,
        string externalTransactionId,
        CancellationToken ct = default)
    {
        try
        {
            var client = new StripeClient(secretKey);
            var refundService = new RefundService(client);

            var options = new RefundCreateOptions
            {
                PaymentIntent = externalTransactionId
            };
            var refund = await refundService.CreateAsync(options, cancellationToken: ct);
            var success = refund.Status is "succeeded" or "pending";
            return new TenantPaymentResult(success, refund.Id, success ? null : $"Refund status: {refund.Status}");
        }
        catch (StripeException ex)
        {
            return new TenantPaymentResult(false, null, ex.StripeError?.Message ?? ex.Message);
        }
    }
}
