using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio del aggregate <see cref="Domain.Wallet.Wallet"/> + su ledger (append-only). Filtra por
/// <c>tenantId</c> explícito (aislamiento a nivel de repo; el filtro global fail-closed del DbContext es
/// la red de seguridad).
/// </summary>
public sealed class WalletRepository(WalletDbContext db) : IWalletRepository
{
    // IgnoreQueryFilters(): el tenantId viene explícito y validado desde Application (mismo criterio que
    // Notes/Campaigns). El filtro global fail-closed es red de seguridad para queries fuera del repo; en
    // un handler de Wolverine el TenantContext puede no estar poblado y excluiría filas legítimas.
    public Task<Domain.Wallet.Wallet?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        db.Wallets.IgnoreQueryFilters().FirstOrDefaultAsync(w => w.TenantId == tenantId, ct);

    public async Task AddAsync(Domain.Wallet.Wallet wallet, CancellationToken ct = default) =>
        await db.Wallets.AddAsync(wallet, ct);

    public async Task AddLedgerEntryAsync(LedgerEntry entry, CancellationToken ct = default) =>
        await db.LedgerEntries.AddAsync(entry, ct);

    public async Task<PagedResult<LedgerEntry>> ListLedgerAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = db
            .LedgerEntries.IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.CreatedAtUtc);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<LedgerEntry>(items, page, size, total);
    }

    // ---------- F2: recargas ----------

    public async Task AddTopUpAsync(WalletTopUp topUp, CancellationToken ct = default) =>
        await db.WalletTopUps.AddAsync(topUp, ct);

    public Task<WalletTopUp?> GetTopUpAsync(Guid tenantId, Guid topUpId, CancellationToken ct = default) =>
        db.WalletTopUps.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == topUpId, ct);

    public Task<bool> FundingCreditExistsAsync(string sourceService, Guid saaSPaymentId, CancellationToken ct = default) =>
        db.FundingCredits.IgnoreQueryFilters()
            .AnyAsync(c => c.SourceService == sourceService && c.SaaSPaymentId == saaSPaymentId, ct);

    public async Task AddFundingCreditAsync(FundingCredit credit, CancellationToken ct = default) =>
        await db.FundingCredits.AddAsync(credit, ct);
}
