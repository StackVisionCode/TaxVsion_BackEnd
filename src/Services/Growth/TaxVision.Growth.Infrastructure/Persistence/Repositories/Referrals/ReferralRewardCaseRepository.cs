using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;

public sealed class ReferralRewardCaseRepository(GrowthDbContext dbContext, ITenantContext tenantContext)
    : IReferralRewardCaseRepository
{
    public Task<ReferralRewardCase?> GetByIdAsync(
        Guid rewardCaseId,
        Guid ownerTenantId,
        CancellationToken ct = default
    ) => FindOwnedAsync(ownerTenantId, rewardCase => rewardCase.Id == rewardCaseId, rewardCaseId, ct);

    public Task<ReferralRewardCase?> GetByGrantIdAsync(
        Guid grantId,
        Guid ownerTenantId,
        CancellationToken ct = default
    ) => FindOwnedAsync(ownerTenantId, rewardCase => rewardCase.GrantId == grantId, grantId, ct);

    public Task<ReferralRewardCase?> GetForCompensationAsync(Guid rewardCaseId, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant || tenantContext.TenantId == Guid.Empty || rewardCaseId == Guid.Empty)
            return Task.FromResult<ReferralRewardCase?>(null);

        // Payment-originated compensation is the only cross-tenant reward lookup.
        // Authentication/authorization belongs to the message or endpoint boundary;
        // persistence elevates only this exact aggregate ID.
        return dbContext
            .ReferralRewardCases.IgnoreQueryFilters()
            .SingleOrDefaultAsync(rewardCase => rewardCase.Id == rewardCaseId, ct);
    }

    public async Task AddAsync(ReferralRewardCase rewardCase, CancellationToken ct = default)
    {
        if (!TenantRepositoryGuard.Matches(tenantContext, rewardCase.TenantId))
        {
            var sourceAttributionIsTrackedForActiveTenant =
                tenantContext.HasTenant
                && rewardCase.BeneficiaryType == ReferralParticipantType.Tenant
                && rewardCase.BeneficiaryId == rewardCase.TenantId
                && dbContext
                    .ChangeTracker.Entries<ReferralAttribution>()
                    .Any(entry =>
                        entry.Entity.Id == rewardCase.AttributionId
                        && entry.Entity.TenantId == tenantContext.TenantId
                        && entry.Entity.ReferrerId == rewardCase.TenantId
                    );

            if (!sourceAttributionIsTrackedForActiveTenant)
                TenantRepositoryGuard.EnsureMatches(tenantContext, rewardCase.TenantId);
        }

        await dbContext.ReferralRewardCases.AddAsync(rewardCase, ct);
    }

    private Task<ReferralRewardCase?> FindOwnedAsync(
        Guid ownerTenantId,
        System.Linq.Expressions.Expression<Func<ReferralRewardCase, bool>> predicate,
        Guid requiredId,
        CancellationToken ct
    ) =>
        requiredId == Guid.Empty || !TenantRepositoryGuard.Matches(tenantContext, ownerTenantId)
            ? Task.FromResult<ReferralRewardCase?>(null)
            : dbContext
                .ReferralRewardCases.IgnoreQueryFilters()
                .Where(rewardCase => rewardCase.TenantId == ownerTenantId)
                .FirstOrDefaultAsync(predicate, ct);
}
