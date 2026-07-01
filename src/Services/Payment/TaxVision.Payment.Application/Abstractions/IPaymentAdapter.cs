namespace TaxVision.Payment.Application.Abstractions;

public sealed record TenantPaymentResult(bool IsSuccess, string? ExternalTransactionId, string? FailureReason);

public interface IPaymentAdapter
{
    Task<TenantPaymentResult> ChargeAsync(string secretKey, long amountCents, string currency, string description, CancellationToken ct = default);
    Task<TenantPaymentResult> RefundAsync(string secretKey, string externalTransactionId, CancellationToken ct = default);
}
