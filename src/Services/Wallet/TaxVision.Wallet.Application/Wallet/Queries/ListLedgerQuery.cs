using BuildingBlocks.Common;
using TaxVision.Wallet.Application.Wallet.Abstractions;

namespace TaxVision.Wallet.Application.Wallet.Queries;

/// <summary>Historial del ledger del tenant, paginado, más recientes primero.</summary>
public sealed record ListLedgerQuery(Guid TenantId, int Page, int Size);

public static class ListLedgerHandler
{
    public static async Task<PagedResult<LedgerEntryView>> Handle(
        ListLedgerQuery query,
        IWalletRepository wallets,
        CancellationToken ct
    )
    {
        var page = await wallets.ListLedgerAsync(query.TenantId, query.Page, query.Size, ct);
        var items = page.Items.Select(LedgerEntryView.From).ToList();
        return new PagedResult<LedgerEntryView>(items, page.Page, page.Size, page.TotalCount);
    }
}
