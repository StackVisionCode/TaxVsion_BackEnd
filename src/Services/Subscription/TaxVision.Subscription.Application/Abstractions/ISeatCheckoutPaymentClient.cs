using BuildingBlocks.Results;

namespace TaxVision.Subscription.Application.Abstractions;

public sealed record SeatCheckoutClientRequest(
    Guid TenantId,
    Guid SeatPurchaseIntentId,
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

public sealed record SeatCheckoutClientResult(
    Guid PaymentId,
    string CheckoutUrl,
    string ProviderSessionId,
    DateTime ExpiresAtUtc
);

/// <summary>Estado de un pago de asientos tal como lo ve PaymentApp — lo usa el reconcile.</summary>
public sealed record SeatPaymentStatusResult(string Status, DateTime? PaidAtUtc);

/// <summary>Cliente M2M contra PaymentApp para crear la sesión de checkout hosteada de una compra de asientos
/// (<c>POST internal/seats/checkout</c>) y consultar el estado de un pago (<c>GET internal/seats/payments/{id}</c>,
/// para el reconcile). Molde: el cliente de onboarding Auth→PaymentApp.</summary>
public interface ISeatCheckoutPaymentClient
{
    Task<Result<SeatCheckoutClientResult>> CreateCheckoutAsync(
        SeatCheckoutClientRequest request,
        CancellationToken ct = default
    );

    /// <summary>Estado del pago de una intención. <c>null</c> si PaymentApp no responde o no existe — el
    /// reconcile trata eso como "todavía no confirmado" y reintenta en el próximo tick.</summary>
    Task<SeatPaymentStatusResult?> GetPaymentStatusAsync(
        Guid tenantId,
        Guid saaSPaymentId,
        CancellationToken ct = default
    );
}
