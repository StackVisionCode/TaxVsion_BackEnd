using BuildingBlocks.Results;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Application.Wallet.Queries;

/// <summary>Estado de una orden de recarga (pago + acreditación).</summary>
public sealed record GetTopUpQuery(Guid TenantId, Guid TopUpId);

public static class GetTopUpHandler
{
    public static async Task<Result<TopUpView>> Handle(
        GetTopUpQuery query,
        IWalletRepository wallets,
        CancellationToken ct
    )
    {
        var topUp = await wallets.GetTopUpAsync(query.TenantId, query.TopUpId, ct);
        return topUp is null
            ? Result.Failure<TopUpView>(WalletErrors.NotFound)
            : Result.Success(TopUpView.From(topUp));
    }
}
