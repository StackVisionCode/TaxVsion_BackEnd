using BuildingBlocks.Results;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Domain.Wallet;
using Wolverine.Attributes;

namespace TaxVision.Wallet.Application.Wallet.Commands;

/// <summary>Pide a PaymentApp la sesión de pago de una orden ya confirmada. Un reintento manda la misma clave de
/// la orden, y PaymentApp devuelve la misma sesión.</summary>
public sealed record StartTopUpCheckoutCommand(
    Guid TenantId,
    Guid TopUpId,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string Provider,
    string Method
);

public static class StartTopUpCheckoutHandler
{
    // Sin IUnitOfWork ni transacción: la llamada a PaymentApp no retiene una transacción abierta.
    [NonTransactional]
    public static async Task<Result<WalletTopUpCheckoutClientResult>> Handle(
        StartTopUpCheckoutCommand command,
        IWalletRepository wallets,
        IWalletTopUpCheckoutClient checkout,
        CancellationToken ct
    )
    {
        var topUp = await wallets.GetTopUpReadOnlyAsync(command.TenantId, command.TopUpId, ct);
        if (topUp is null)
            return Result.Failure<WalletTopUpCheckoutClientResult>(WalletErrors.NotFound);

        if (topUp.Status != WalletTopUpStatus.Pending)
            return Result.Failure<WalletTopUpCheckoutClientResult>(WalletErrors.TopUpNotPending);

        return await checkout.CreateCheckoutAsync(
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
    }
}
