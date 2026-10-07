using BuildingBlocks.Results;

namespace TaxVision.Wallet.Application.Wallet.Abstractions;

/// <summary>Pedido de crear la sesión de checkout hosteada de una recarga (money-IN) contra PaymentApp.</summary>
public sealed record WalletTopUpCheckoutClientRequest(
    Guid TenantId,
    Guid TopUpId,
    long AmountCents,
    string Currency,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    string Provider,
    string Method
);

/// <summary>Sesión creada: la URL del proveedor a la que el front redirige, + referencia e expiración.</summary>
public sealed record WalletTopUpCheckoutClientResult(
    Guid PaymentId,
    string CheckoutUrl,
    string ProviderSessionId,
    DateTime ExpiresAtUtc
);

/// <summary>
/// Puerto M2M hacia PaymentApp para crear el checkout <b>hosteado</b> de una recarga (00_Plan §6, modelo
/// sin tarjeta guardada): el tenant paga en Stripe/PayPal por redirect y, al confirmarse el pago, PaymentApp
/// publica <c>WalletTopUpPaymentSucceeded</c> que el Wallet ya consume para acreditar. Reemplaza el disparo
/// off-session (<c>WalletTopUpDueIntegrationEvent</c>).
/// </summary>
public interface IWalletTopUpCheckoutClient
{
    Task<Result<WalletTopUpCheckoutClientResult>> CreateCheckoutAsync(
        WalletTopUpCheckoutClientRequest request,
        CancellationToken ct = default
    );
}
