using BuildingBlocks.Results;

namespace TaxVision.Subscription.Application.Abstractions;

public sealed record RenewalCheckoutClientRequest(
    Guid TenantId,
    Guid RenewalIntentId,
    long AmountCents,
    string Currency,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    string Provider,
    string Method
);

public sealed record RenewalCheckoutClientResult(
    Guid PaymentId,
    string CheckoutUrl,
    string ProviderSessionId,
    DateTime ExpiresAtUtc
);

/// <summary>Estado de un pago de renovación tal como lo ve PaymentApp — lo usa el reconcile.</summary>
public sealed record RenewalPaymentStatusResult(string Status, DateTime? PaidAtUtc);

/// <summary>Cliente M2M contra PaymentApp para crear la sesión de checkout hosteada de una renovación
/// self-service (<c>POST internal/subscription-renewal/checkout</c>) y consultar el estado de un pago
/// (<c>GET internal/subscription-renewal/payments/{id}</c>, para el reconcile). Molde:
/// <see cref="ISeatCheckoutPaymentClient"/>.</summary>
public interface IRenewalCheckoutPaymentClient
{
    Task<Result<RenewalCheckoutClientResult>> CreateCheckoutAsync(
        RenewalCheckoutClientRequest request,
        CancellationToken ct = default
    );

    /// <summary>Estado del pago de una intención. <c>null</c> si PaymentApp no responde o no existe — el
    /// reconcile lo trata como "todavía no confirmado" y reintenta en el próximo tick.</summary>
    Task<RenewalPaymentStatusResult?> GetPaymentStatusAsync(
        Guid tenantId,
        Guid saaSPaymentId,
        CancellationToken ct = default
    );
}
