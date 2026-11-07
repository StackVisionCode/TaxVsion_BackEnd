using BuildingBlocks.Common;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Domain.Wallet;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.TestDoubles;

/// <summary>IWalletRepository en memoria: monederos, asientos, recargas y créditos en listas.</summary>
public sealed class FakeWalletRepository : IWalletRepository
{
    public List<WalletAggregate> Wallets { get; } = [];
    public List<LedgerEntry> LedgerEntries { get; } = [];
    public List<WalletTopUp> TopUps { get; } = [];
    public List<FundingCredit> FundingCredits { get; } = [];
    public List<WalletBalanceMismatch> BalanceMismatches { get; } = [];

    public Task<WalletAggregate?> GetByTenantAsync(Guid tenantId, string currency, CancellationToken ct = default) =>
        Task.FromResult(Wallets.FirstOrDefault(w => w.TenantId == tenantId && w.Currency == currency));

    public Task AddAsync(WalletAggregate wallet, CancellationToken ct = default)
    {
        Wallets.Add(wallet);
        return Task.CompletedTask;
    }

    public Task AddLedgerEntryAsync(LedgerEntry entry, CancellationToken ct = default)
    {
        LedgerEntries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<PagedResult<LedgerEntry>> ListLedgerAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var all = LedgerEntries.Where(e => e.TenantId == tenantId).OrderByDescending(e => e.CreatedAtUtc).ToList();
        var items = all.Skip((page - 1) * size).Take(size).ToList();
        return Task.FromResult(new PagedResult<LedgerEntry>(items, page, size, all.Count));
    }

    public Task<IReadOnlyList<WalletBalanceMismatch>> ListBalanceMismatchesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<WalletBalanceMismatch>>(BalanceMismatches);

    public Task AddTopUpAsync(WalletTopUp topUp, CancellationToken ct = default)
    {
        TopUps.Add(topUp);
        return Task.CompletedTask;
    }

    public Task<WalletTopUp?> GetTopUpAsync(Guid tenantId, Guid topUpId, CancellationToken ct = default) =>
        Task.FromResult(TopUps.FirstOrDefault(t => t.TenantId == tenantId && t.Id == topUpId));

    public Task<WalletTopUp?> GetTopUpReadOnlyAsync(Guid tenantId, Guid topUpId, CancellationToken ct = default) =>
        GetTopUpAsync(tenantId, topUpId, ct);

    public Task<bool> FundingCreditExistsAsync(
        string sourceService,
        Guid saaSPaymentId,
        CancellationToken ct = default
    ) => Task.FromResult(FundingCredits.Any(c => c.SourceService == sourceService && c.SaaSPaymentId == saaSPaymentId));

    public Task AddFundingCreditAsync(FundingCredit credit, CancellationToken ct = default)
    {
        FundingCredits.Add(credit);
        return Task.CompletedTask;
    }
}
