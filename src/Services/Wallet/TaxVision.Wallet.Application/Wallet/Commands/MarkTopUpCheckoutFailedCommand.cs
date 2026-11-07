using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Application.Wallet.Commands;

/// <summary>Cierra una orden cuyo checkout no se pudo abrir, para que no quede <c>Pending</c> para siempre.
/// La orden sigue pudiendo acreditarse: <see cref="WalletTopUp.MarkCredited"/> no depende de este estado.</summary>
public sealed record MarkTopUpCheckoutFailedCommand(Guid TenantId, Guid TopUpId, string Reason);

public static class MarkTopUpCheckoutFailedHandler
{
    public static async Task<Result> Handle(
        MarkTopUpCheckoutFailedCommand command,
        IWalletRepository wallets,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var topUp = await wallets.GetTopUpAsync(command.TenantId, command.TopUpId, ct);
        if (topUp is null)
            return Result.Failure(WalletErrors.NotFound);

        // Otro camino pudo cerrarla mientras tanto (el resultado del pago gana).
        if (topUp.Status != WalletTopUpStatus.Pending)
            return Result.Success();

        topUp.MarkFailed(command.Reason);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
