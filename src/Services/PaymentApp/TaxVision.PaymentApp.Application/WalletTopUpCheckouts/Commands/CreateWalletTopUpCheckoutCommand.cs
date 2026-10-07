using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.WalletTopUpCheckouts.Commands;

/// <summary>
/// Crea (o replay idempotente) una sesión de checkout <b>hosteada</b> para recargar el monedero de un tenant.
/// Molde: <c>CreateSeatsCheckoutCommand</c>. El Wallet (dueño del monto) ya fijó <see cref="AmountCents"/>/
/// <see cref="Currency"/>; acá NO se resuelve precio. <see cref="TopUpId"/> viaja como <c>TargetAggregateId</c>
/// del pago y correlaciona el webhook con la orden de recarga que el Wallet acredita al confirmarse el pago.
/// NUNCA se cobra off-session ni se guarda tarjeta: el cliente paga en el proveedor (Stripe/PayPal) por redirect.
/// </summary>
public sealed record CreateWalletTopUpCheckoutCommand(
    Guid TenantId,
    Guid TopUpId,
    long AmountCents,
    string Currency,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    PaymentProviderCode Provider = PaymentProviderCode.Stripe,
    PaymentMethodKind Method = PaymentMethodKind.Card
);

public sealed record WalletTopUpCheckoutResponse(
    Guid PaymentId,
    string CheckoutUrl,
    string ProviderSessionId,
    DateTime ExpiresAtUtc
);
