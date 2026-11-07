using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Application.Wallet.Commands;

/// <summary>Crea la orden de recarga en <see cref="WalletTopUpStatus.Pending"/> y la confirma, antes de llamar a
/// PaymentApp: la intención queda guardada con su clave <c>wallet-topup:{id:N}</c>.</summary>
public sealed record CreateTopUpOrderCommand(Guid TenantId, Guid RequestedByUserId, long AmountCents, string Currency);

public static class CreateTopUpOrderHandler
{
    public static async Task<Result<TopUpView>> Handle(
        CreateTopUpOrderCommand command,
        IWalletRepository wallets,
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
            return Result.Failure<TopUpView>(created.Error);

        await wallets.AddTopUpAsync(created.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TopUpView.From(created.Value));
    }
}
