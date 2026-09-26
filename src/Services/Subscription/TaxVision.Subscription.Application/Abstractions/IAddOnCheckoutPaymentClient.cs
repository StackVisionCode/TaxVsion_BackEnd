using BuildingBlocks.Results;

namespace TaxVision.Subscription.Application.Abstractions;

public sealed record AddOnCheckoutClientRequest(
    Guid TenantId,
    Guid AddOnPurchaseIntentId,
    long AmountCents,
    string Currency,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    string Provider,
    string Method,
    /// <summary>Unidades y precio unitario, para el recibo. PaymentApp los ignora si no cuadran.</summary>
    int? Quantity = null,
    long? UnitAmountCents = null
);

public sealed record AddOnCheckoutClientResult(
    Guid PaymentId,
    string CheckoutUrl,
    string ProviderSessionId,
    DateTime ExpiresAtUtc
);

public sealed record AddOnPaymentStatusResult(string Status, DateTime? PaidAtUtc);

/// <summary>M2M contra PaymentApp para el hosted-checkout de la compra de un add-on. Espejo de
/// <see cref="ISeatCheckoutPaymentClient"/>.</summary>
public interface IAddOnCheckoutPaymentClient
{
    Task<Result<AddOnCheckoutClientResult>> CreateCheckoutAsync(
        AddOnCheckoutClientRequest request,
        CancellationToken ct = default
    );

    Task<AddOnPaymentStatusResult?> GetPaymentStatusAsync(
        Guid tenantId,
        Guid saaSPaymentId,
        CancellationToken ct = default
    );
}
