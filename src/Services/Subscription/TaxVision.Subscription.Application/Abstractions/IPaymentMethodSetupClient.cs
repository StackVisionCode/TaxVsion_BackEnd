using BuildingBlocks.Results;

namespace TaxVision.Subscription.Application.Abstractions;

public sealed record PaymentMethodSetupClientRequest(
    Guid TenantId,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    string? Provider
);

public sealed record PaymentMethodSetupClientResult(string CheckoutUrl, DateTime ExpiresAtUtc);

/// <summary>Cliente M2M contra PaymentApp (<c>POST internal/payment-methods/setup-session</c>) para abrir
/// una sesión que solo captura una tarjeta y la deja como default off-session — sin cobro. Molde:
/// <see cref="IRenewalCheckoutPaymentClient"/>.</summary>
public interface IPaymentMethodSetupClient
{
    Task<Result<PaymentMethodSetupClientResult>> CreateSetupSessionAsync(
        PaymentMethodSetupClientRequest request,
        CancellationToken ct = default
    );
}
