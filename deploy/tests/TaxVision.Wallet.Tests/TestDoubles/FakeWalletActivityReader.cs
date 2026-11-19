using BuildingBlocks.Common;
using TaxVision.Wallet.Application.Activity.Abstractions;
using TaxVision.Wallet.Domain.Pricing;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Tests.TestDoubles;

/// <summary>Lectura en memoria: devuelve las filas sembradas y recuerda el último filtro recibido.</summary>
public sealed class FakeWalletActivityReader : IWalletActivityReader
{
    public List<LedgerEntryRow> Rows { get; } = [];
    public List<SpendRow> Spend { get; } = [];
    public Dictionary<int, IReadOnlyDictionary<PriceChannel, long>> Prices { get; } = [];
    public TransactionFilter? LastFilter { get; private set; }

    public Task<PagedResult<LedgerEntryRow>> ListAsync(
        Guid tenantId,
        TransactionFilter filter,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        LastFilter = filter;
        var all = Rows.Where(r => r.Entry.TenantId == tenantId).ToList();
        return Task.FromResult(
            new PagedResult<LedgerEntryRow>(all.Skip((page - 1) * size).Take(size).ToList(), page, size, all.Count)
        );
    }

    public Task<LedgerEntryRow?> GetAsync(Guid tenantId, Guid entryId, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.Entry.TenantId == tenantId && r.Entry.Id == entryId));

    public Task<IReadOnlyList<LedgerEntry>> ListByReferenceAsync(
        Guid tenantId,
        string? referenceType,
        Guid referenceId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<LedgerEntry>>(
            Rows.Select(r => r.Entry)
                .Where(e => e.TenantId == tenantId && e.ReferenceId == referenceId)
                .Where(e => referenceType is null || e.ReferenceType == referenceType)
                .OrderBy(e => e.CreatedAtUtc)
                .ToList()
        );

    public Task<IReadOnlyList<SpendRow>> ListSpendAsync(
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtcExclusive,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<SpendRow>>(
            Spend.Where(s => s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc < toUtcExclusive).ToList()
        );

    public Task<IReadOnlyDictionary<int, IReadOnlyDictionary<PriceChannel, long>>> GetUnitPricesAsync(
        IReadOnlyCollection<int> versions,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyDictionary<int, IReadOnlyDictionary<PriceChannel, long>>>(
            Prices.Where(p => versions.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value)
        );
}
