using BuildingBlocks.Common;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;

/// <summary>
/// Lecturas de administración de referidos. Solo con el tenant de PLATAFORMA en contexto (el PlatformAdmin): cualquier
/// otro contexto recibe vacío. Cruza tenants de forma explícita; nunca expone el código en claro.
/// </summary>
public sealed class ReferralAdminReadModel(GrowthDbContext db, ITenantContext tenantContext) : IReferralAdminReadModel
{
    public async Task<PagedResult<ReferralCodeAdminRow>> ListCodesAsync(
        Guid? ownerTenantId,
        ReferralCodeStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        if (!TenantRepositoryGuard.Matches(tenantContext, PlatformTenant.Id))
            return new PagedResult<ReferralCodeAdminRow>([], page, size, 0);

        var codes = db.ReferralCodes.IgnoreQueryFilters().AsNoTracking().AsQueryable();
        if (ownerTenantId is { } owner)
            codes = codes.Where(code => code.OwnerId == owner);
        if (status is { } wanted)
            codes = codes.Where(code => code.Status == wanted);

        var total = await codes.CountAsync(ct);
        var items = await codes
            .OrderByDescending(code => code.CreatedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(code => new ReferralCodeAdminRow(
                code.Id,
                code.OwnerId,
                code.OwnerUserId,
                code.DisplayPrefix,
                code.LastFour,
                code.Status,
                code.CreatedAtUtc,
                code.ExpiresAtUtc,
                code.RevokedAtUtc,
                code.RevocationReason,
                db.ReferralAttributions.IgnoreQueryFilters().Count(a => a.ReferralCodeId == code.Id)
            ))
            .ToListAsync(ct);

        return new PagedResult<ReferralCodeAdminRow>(items, page, size, total);
    }

    public async Task<PagedResult<ReferralRewardAdminRow>> ListRewardsAsync(
        ReferralRewardCaseStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        if (!TenantRepositoryGuard.Matches(tenantContext, PlatformTenant.Id))
            return new PagedResult<ReferralRewardAdminRow>([], page, size, 0);

        var rewards = db.ReferralRewardCases.IgnoreQueryFilters().AsNoTracking().AsQueryable();
        if (status is { } wanted)
            rewards = rewards.Where(reward => reward.Status == wanted);

        var total = await rewards.CountAsync(ct);
        var items = await rewards
            .OrderByDescending(reward => reward.CreatedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(reward => new ReferralRewardAdminRow(
                reward.Id,
                reward.BeneficiaryId,
                reward.PricePaidCents,
                reward.RewardAmountCents,
                reward.RewardCurrency,
                reward.Status,
                reward.StateReason,
                reward.EligibleAtUtc,
                reward.CreatedAtUtc
            ))
            .ToListAsync(ct);

        return new PagedResult<ReferralRewardAdminRow>(items, page, size, total);
    }
}
