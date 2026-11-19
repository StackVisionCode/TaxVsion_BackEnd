using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Wallet.Application.Activity;
using TaxVision.Wallet.Application.Activity.Abstractions;
using TaxVision.Wallet.Application.Settings.Abstractions;

namespace TaxVision.Wallet.Application.Wallet.Queries;

/// <summary>
/// Historial del ledger del tenant, paginado, más recientes primero. <paramref name="From"/>/<paramref name="To"/>:
/// rango [desde, hasta): instante ISO (hasta no entra) o "yyyy-MM-dd" (día de la oficina; hasta lo incluye).
/// </summary>
public sealed record ListLedgerQuery(
    Guid TenantId,
    int Page,
    int Size,
    TransactionKind Kind = TransactionKind.All,
    string? From = null,
    string? To = null
);

public static class ListLedgerHandler
{
    public static async Task<Result<PagedResult<LedgerEntryView>>> Handle(
        ListLedgerQuery query,
        IWalletActivityReader reader,
        IOfficeTimeZone officeTimeZone,
        CancellationToken ct
    )
    {
        var zone = await officeTimeZone.ResolveAsync(query.TenantId, ct);
        var range = OfficeDates.ToUtcRange(query.From, query.To, zone);
        if (range.IsFailure)
            return Result.Failure<PagedResult<LedgerEntryView>>(range.Error);

        var filter = new TransactionFilter(query.Kind, range.Value.FromUtc, range.Value.ToUtcExclusive);
        var page = await reader.ListAsync(query.TenantId, filter, query.Page, query.Size, ct);
        var items = page.Items.Select(LedgerEntryView.From).ToList();
        return Result.Success(new PagedResult<LedgerEntryView>(items, page.Page, page.Size, page.TotalCount));
    }
}
