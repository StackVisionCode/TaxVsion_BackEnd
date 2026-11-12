using BuildingBlocks.Common;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Rewards;
using TaxVision.Referrals.Domain.Shares;

namespace TaxVision.Referrals.Application.Abstractions;

/// <summary>Una invitación (atribución) de la oficina referidora, con el estado de su premio.</summary>
public sealed record ReferralInviteRow(
    Guid AttributionId,
    Guid RefereeReference,
    ReferralAttributionStatus Status,
    ReferralShareChannel? Channel,
    Guid? ShareId,
    DateTime AttributedAtUtc,
    DateTime? QualifiedAtUtc,
    long? PricePaidCents,
    long? RewardAmountCents,
    string? RewardCurrency,
    ReferralRewardCaseStatus? RewardStatus,
    DateTime? RewardEligibleAtUtc
);

/// <summary>Un envío del código con los registros y pagos que produjo.</summary>
public sealed record ReferralShareRow(
    Guid ShareId,
    Guid ReferralCodeId,
    ReferralShareChannel Channel,
    Guid SharedByUserId,
    DateTime SharedAtUtc,
    int Registrations,
    int Payments
);

/// <summary>Totales para la cabecera de "Mis referidos".</summary>
public sealed record ReferralRewardSummary(
    long TotalEarnedCents,
    long PendingCents,
    int RegisteredCount,
    int CreditedCount,
    string Currency
);

/// <summary>
/// Modelo de lectura de referidos de UNA oficina (CQRS: las consultas no pasan por los repositorios de los aggregates).
/// Todas las operaciones están acotadas al tenant referidor; el tenant activo debe coincidir con el pedido.
/// </summary>
public interface IReferralReadModel
{
    Task<PagedResult<ReferralInviteRow>> ListInvitesAsync(
        Guid referrerTenantId,
        int page,
        int size,
        CancellationToken ct = default
    );

    Task<PagedResult<ReferralShareRow>> ListSharesAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct = default
    );

    Task<ReferralRewardSummary> GetSummaryAsync(Guid tenantId, CancellationToken ct = default);
}
