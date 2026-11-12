using BuildingBlocks.Common;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Abstractions;

/// <summary>Un código de referido visto por la plataforma (nunca el código en claro: solo su huella).</summary>
public sealed record ReferralCodeAdminRow(
    Guid CodeId,
    Guid OwnerTenantId,
    Guid? OwnerUserId,
    string DisplayPrefix,
    string LastFour,
    ReferralCodeStatus Status,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? RevokedAtUtc,
    string? RevocationReason,
    int Registrations
);

/// <summary>Un premio visto por la plataforma para su revisión: de qué oficina es, cuánto es y en qué estado está.</summary>
public sealed record ReferralRewardAdminRow(
    Guid RewardCaseId,
    Guid ReferrerTenantId,
    long PricePaidCents,
    long RewardAmountCents,
    string? RewardCurrency,
    ReferralRewardCaseStatus Status,
    string? StateReason,
    DateTime EligibleAtUtc,
    DateTime CreatedAtUtc
);

/// <summary>
/// Lecturas de administración de referidos (PlatformAdmin). Cruzan tenants de forma explícita; solo operan con el tenant
/// de PLATAFORMA en contexto. Solo lectura, sin tracking.
/// </summary>
public interface IReferralAdminReadModel
{
    Task<PagedResult<ReferralCodeAdminRow>> ListCodesAsync(
        Guid? ownerTenantId,
        ReferralCodeStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    );

    Task<PagedResult<ReferralRewardAdminRow>> ListRewardsAsync(
        ReferralRewardCaseStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    );
}
