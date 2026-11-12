using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Abstractions;

/// <summary>Referencia mínima a un caso de premio listo para acreditar (el worker actúa por caso, con su tenant).</summary>
public sealed record GrantableRewardCaseRef(Guid TenantId, Guid RewardCaseId);

public interface IReferralRewardCaseRepository
{
    /// <summary>
    /// Casos <c>WalletCredit</c> en <c>Requested</c> (o <c>PendingGrant</c> sin respuesta del Wallet hace rato) cuya espera ya venció, del más antiguo al más
    /// nuevo. Escaneo de sistema (cruza tenants) solo para el worker de premios — nunca para consultas interactivas.
    /// </summary>
    Task<IReadOnlyList<GrantableRewardCaseRef>> GetGrantableWalletCreditCasesAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );

    /// <summary>
    /// Casos <c>WalletCredit</c> en <c>ClawbackPending</c> cuya reversa lleva un rato sin respuesta del Wallet: el worker
    /// la vuelve a pedir. Mismo escaneo de sistema que <see cref="GetGrantableWalletCreditCasesAsync"/>.
    /// </summary>
    Task<IReadOnlyList<GrantableRewardCaseRef>> GetStaleClawbackPendingCasesAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );

    Task<ReferralRewardCase?> GetByIdAsync(Guid rewardCaseId, Guid ownerTenantId, CancellationToken ct = default);

    Task<ReferralRewardCase?> GetByGrantIdAsync(Guid grantId, Guid ownerTenantId, CancellationToken ct = default);

    /// <summary>
    /// Lookup elevado y exacto para compensaciones originadas por Payment. El caller M2M
    /// ya debe haber sido autorizado; nunca se usa para consultas interactivas.
    /// </summary>
    Task<ReferralRewardCase?> GetForCompensationAsync(Guid rewardCaseId, CancellationToken ct = default);

    /// <summary>
    /// Premio generado por un pago concreto (el pago del REFERIDO que lo calificó). Lookup elevado y exacto, solo para
    /// compensaciones originadas por Payment (reembolso/chargeback); requiere un tenant en contexto y nunca se usa en
    /// consultas interactivas.
    /// </summary>
    Task<ReferralRewardCase?> GetByPaymentAsync(Guid paymentId, CancellationToken ct = default);

    Task AddAsync(ReferralRewardCase rewardCase, CancellationToken ct = default);
}
