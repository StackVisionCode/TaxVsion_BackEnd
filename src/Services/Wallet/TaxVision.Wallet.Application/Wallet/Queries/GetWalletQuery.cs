using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Wallet.Application.Wallet.Abstractions;

namespace TaxVision.Wallet.Application.Wallet.Queries;

/// <summary>Saldo del monedero del tenant. Auto-crea un monedero con saldo 0 si aún no existe (§8).</summary>
public sealed record GetWalletQuery(Guid TenantId);

public static class GetWalletHandler
{
    public static async Task<Result<WalletView>> Handle(
        GetWalletQuery query,
        IWalletRepository wallets,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var wallet = await wallets.GetByTenantAsync(query.TenantId, ct);
        if (wallet is not null)
            return Result.Success(WalletView.From(wallet));

        // Auto-create (zero-balance). F1: sin manejo de carrera concurrente (el índice único por
        // tenant la detectaría; reconciliación de la carrera queda para F2).
        var created = Domain.Wallet.Wallet.Create(query.TenantId);
        if (created.IsFailure)
            return Result.Failure<WalletView>(created.Error);

        await wallets.AddAsync(created.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(WalletView.From(created.Value));
    }
}
