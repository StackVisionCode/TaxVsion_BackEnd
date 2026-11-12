using BuildingBlocks.Common;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;

/// <summary>
/// Lecturas de referidos de una oficina. Las atribuciones pertenecen al tenant REFERIDO (el del alta), no al
/// referidor, por eso se leen con <c>IgnoreQueryFilters</c> y se acotan EXPLÍCITAMENTE por <c>ReferrerId</c> = tenant
/// del llamador (verificado contra el tenant activo con <see cref="TenantRepositoryGuard"/>). Solo lectura, sin tracking.
/// </summary>
public sealed class ReferralReadModel(GrowthDbContext db, ITenantContext tenantContext) : IReferralReadModel
{
    private static readonly ReferralRewardCaseStatus[] CreditedStatuses =
    [
        ReferralRewardCaseStatus.Granted,
        ReferralRewardCaseStatus.Vested,
    ];

    private static readonly ReferralRewardCaseStatus[] PendingStatuses =
    [
        ReferralRewardCaseStatus.Requested,
        ReferralRewardCaseStatus.PendingGrant,
        ReferralRewardCaseStatus.ManualReview,
    ];

    public async Task<PagedResult<ReferralInviteRow>> ListInvitesAsync(
        Guid referrerTenantId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        if (!TenantRepositoryGuard.Matches(tenantContext, referrerTenantId))
            return new PagedResult<ReferralInviteRow>([], page, size, 0);

        var attributions = db
            .ReferralAttributions.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.ReferrerId == referrerTenantId && a.ReferrerType == ReferralParticipantType.Tenant);

        var total = await attributions.CountAsync(ct);
        var items = await (
            from attribution in attributions
            join reward in db.ReferralRewardCases.IgnoreQueryFilters().AsNoTracking()
                on attribution.Id equals reward.AttributionId
                into rewards
            from reward in rewards.DefaultIfEmpty()
            orderby attribution.AttributedAtUtc descending
            select new ReferralInviteRow(
                attribution.Id,
                attribution.RefereeId,
                attribution.Status,
                attribution.Channel,
                attribution.ShareId,
                attribution.AttributedAtUtc,
                attribution.QualifiedAtUtc,
                reward == null ? null : reward.PricePaidCents,
                reward == null ? null : reward.RewardAmountCents,
                reward == null ? null : reward.RewardCurrency,
                reward == null ? null : reward.Status,
                reward == null ? null : reward.EligibleAtUtc
            )
        )
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return new PagedResult<ReferralInviteRow>(items, page, size, total);
    }

    public async Task<PagedResult<ReferralShareRow>> ListSharesAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        if (!TenantRepositoryGuard.Matches(tenantContext, tenantId))
            return new PagedResult<ReferralShareRow>([], page, size, 0);

        var shares = db.ReferralCodeShares.IgnoreQueryFilters().AsNoTracking().Where(s => s.TenantId == tenantId);
        var total = await shares.CountAsync(ct);
        var items = await shares
            .OrderByDescending(share => share.SharedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(share => new ReferralShareRow(
                share.Id,
                share.ReferralCodeId,
                share.Channel,
                share.SharedByUserId,
                share.SharedAtUtc,
                db.ReferralAttributions.IgnoreQueryFilters().Count(a => a.ShareId == share.Id),
                db.ReferralAttributions.IgnoreQueryFilters()
                    .Count(a => a.ShareId == share.Id && a.Status == ReferralAttributionStatus.Qualified)
            ))
            .ToListAsync(ct);

        return new PagedResult<ReferralShareRow>(items, page, size, total);
    }

    public async Task<ReferralRewardSummary> GetSummaryAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (!TenantRepositoryGuard.Matches(tenantContext, tenantId))
            return new ReferralRewardSummary(0, 0, 0, 0, "USD");

        // La fuente de verdad del "ganado" es el caso de premio acreditado (Granted/Vested), no el registro
        // ReferrerBenefit: éste es auditoría y no existe para premios acreditados antes de que se creara.
        // Un premio revertido (Reversed/ClawbackPending) deja de contar.
        var wallet = db
            .ReferralRewardCases.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.RewardType == ReferralRewardType.WalletCredit);

        var creditedCases = wallet.Where(r => CreditedStatuses.Contains(r.Status));
        var earned = await creditedCases.SumAsync(r => (long?)r.RewardAmountCents, ct) ?? 0;
        var credited = await creditedCases.CountAsync(ct);
        var currency =
            await creditedCases
                .Where(r => r.RewardCurrency != null)
                .Select(r => r.RewardCurrency)
                .FirstOrDefaultAsync(ct)
            ?? "USD";

        var pending =
            await wallet.Where(r => PendingStatuses.Contains(r.Status)).SumAsync(r => (long?)r.RewardAmountCents, ct)
            ?? 0;

        var registered = await db
            .ReferralAttributions.IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(a => a.ReferrerId == tenantId && a.ReferrerType == ReferralParticipantType.Tenant, ct);

        return new ReferralRewardSummary(earned, pending, registered, credited, currency);
    }
}
