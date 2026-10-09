using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Application.Wallet.Commands;

/// <summary>
/// Inicia una recarga (00_Plan §6) por <b>checkout hosteado</b> (modelo sin tarjeta guardada): crea la orden
/// <see cref="WalletTopUp"/> (Pending) y pide a PaymentApp una sesión de pago en el proveedor (Stripe/PayPal);
/// devuelve la <c>CheckoutUrl</c> para que el front redirija. NO acredita saldo — eso ocurre cuando el pago se
/// confirma en el proveedor (webhook → <c>WalletTopUpPaymentSucceeded</c> → <see cref="WalletTopUp"/> Credited).
/// </summary>
public sealed record TopUpWalletCommand(
    Guid TenantId,
    Guid RequestedByUserId,
    long AmountCents,
    string Currency,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string Provider = "Stripe",
    string Method = "Card"
);

public static class TopUpWalletHandler
{
    public static async Task<Result<TopUpCheckoutView>> Handle(
        TopUpWalletCommand command,
        IWalletRepository wallets,
        IWalletTopUpCheckoutClient checkout,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var created = WalletTopUp.Create(
            command.TenantId,
            command.AmountCents,
            string.IsNullOrWhiteSpace(command.Currency) ? Domain.Wallet.Wallet.DefaultCurrency : command.Currency,
            command.RequestedByUserId
        );
        if (created.IsFailure)
            return Result.Failure<TopUpCheckoutView>(created.Error);

        var topUp = created.Value;
        await wallets.AddTopUpAsync(topUp, ct);
        await unitOfWork.SaveChangesAsync(ct);

        // Checkout hosteado: PaymentApp crea la sesión en el proveedor y devuelve la URL de pago. El cobro y la
        // acreditación ocurren luego, al confirmarse el pago (webhook → evento → WalletTopUpResultConsumer).
        var session = await checkout.CreateCheckoutAsync(
            new WalletTopUpCheckoutClientRequest(
                topUp.TenantId,
                topUp.Id,
                topUp.AmountCents,
                topUp.Currency,
                command.PayerEmail,
                command.SuccessUrl,
                command.CancelUrl,
                topUp.IdempotencyKey,
                string.IsNullOrWhiteSpace(command.Provider) ? "Stripe" : command.Provider,
                string.IsNullOrWhiteSpace(command.Method) ? "Card" : command.Method
            ),
            ct
        );
        if (session.IsFailure)
            return Result.Failure<TopUpCheckoutView>(session.Error);

        return Result.Success(
            new TopUpCheckoutView(TopUpView.From(topUp), session.Value.CheckoutUrl, session.Value.ExpiresAtUtc)
        );
    }
}
