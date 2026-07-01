namespace TaxVision.Payment.Application.Abstractions;

public sealed record StripePaymentResult(bool IsSuccess, string? PaymentIntentId, string? FailureReason);

public interface IStripeGateway
{
    Task<string> GetOrCreateCustomerAsync(Guid tenantId, string email, CancellationToken ct = default);
    Task<StripePaymentResult> CreatePaymentIntentAsync(string stripeCustomerId, long amountCents, string currency, string description, CancellationToken ct = default);
    Task<StripePaymentResult> ConfirmPaymentIntentAsync(string paymentIntentId, CancellationToken ct = default);
    Task<bool> VerifyWebhookSignatureAsync(string payload, string signature, CancellationToken ct = default);
}
