using BuildingBlocks.Common;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Application.Wallet.Abstractions;

/// <summary>
/// Repositorio del aggregate root <see cref="Domain.Wallet.Wallet"/> (uno por tenant) y de su ledger
/// (append-only). Todas las lecturas por tenant filtran por <c>tenantId</c> explícito (el aislamiento
/// multitenant se hace a nivel de repo).
/// </summary>
public interface IWalletRepository
{
    /// <summary>El monedero del tenant, o <c>null</c> si aún no existe.</summary>
    Task<Domain.Wallet.Wallet?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(Domain.Wallet.Wallet wallet, CancellationToken ct = default);

    /// <summary>Agrega un asiento al ledger (append-only). Lo usa el cobro money-IN/OUT (F2/F3).</summary>
    Task AddLedgerEntryAsync(LedgerEntry entry, CancellationToken ct = default);

    /// <summary>Asientos del ledger del tenant, más recientes primero (paginado).</summary>
    Task<PagedResult<LedgerEntry>> ListLedgerAsync(Guid tenantId, int page, int size, CancellationToken ct = default);

    // ---------- F2: recargas (top-up) ----------

    Task AddTopUpAsync(WalletTopUp topUp, CancellationToken ct = default);

    Task<WalletTopUp?> GetTopUpAsync(Guid tenantId, Guid topUpId, CancellationToken ct = default);

    /// <summary>¿Ya se acreditó este pago? Dedupe por <c>(SourceService, SaaSPaymentId)</c>.</summary>
    Task<bool> FundingCreditExistsAsync(string sourceService, Guid saaSPaymentId, CancellationToken ct = default);

    Task AddFundingCreditAsync(FundingCredit credit, CancellationToken ct = default);
}
