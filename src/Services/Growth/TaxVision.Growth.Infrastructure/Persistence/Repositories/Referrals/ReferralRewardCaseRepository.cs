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

    public Task<ReferralRewardCase?> GetByPaymentAsync(Guid paymentId, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant || tenantContext.TenantId == Guid.Empty || paymentId == Guid.Empty)
            return Task.FromResult<ReferralRewardCase?>(null);

        // El premio es del referidor pero el pago es del referido: único cruce de tenants permitido aquí, por el Id
        // exacto del pago autoritativo (el caller ya fue autorizado en el borde del mensaje).
        return (
            from rewardCase in dbContext.ReferralRewardCases.IgnoreQueryFilters()
            join qualification in dbContext.ReferralQualifications.IgnoreQueryFilters()
                on rewardCase.QualificationId equals qualification.Id
            where qualification.PaymentId == paymentId
            select rewardCase
        ).FirstOrDefaultAsync(ct);
    }

    // Escaneo de sistema (cruza tenants) SOLO para el worker de premios; cada caso se procesa después con su propio
    // tenant en contexto. Devuelve referencias mínimas, nunca entidades de otros tenants.
    public async Task<IReadOnlyList<GrantableRewardCaseRef>> GetGrantableWalletCreditCasesAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    )
    {
        // Una solicitud al Wallet sin respuesta pasado este tiempo se vuelve a pedir (el Wallet deduplica por referencia).
        var staleBefore = nowUtc - WalletResponseGrace;
        return await dbContext
            .ReferralRewardCases.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(rewardCase =>
                rewardCase.RewardType == ReferralRewardType.WalletCredit
                && rewardCase.EligibleAtUtc <= nowUtc
                && (
                    rewardCase.Status == ReferralRewardCaseStatus.Requested
                    || (
                        rewardCase.Status == ReferralRewardCaseStatus.PendingGrant
                        && rewardCase.UpdatedAtUtc <= staleBefore
                    )
                )
            )
            .OrderBy(rewardCase => rewardCase.EligibleAtUtc)
            .Take(batchSize)
            .Select(rewardCase => new GrantableRewardCaseRef(rewardCase.TenantId, rewardCase.Id))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<GrantableRewardCaseRef>> GetStaleClawbackPendingCasesAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    )
    {
        var staleBefore = nowUtc - WalletResponseGrace;
        return await dbContext
            .ReferralRewardCases.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(rewardCase =>
                rewardCase.RewardType == ReferralRewardType.WalletCredit
                && rewardCase.Status == ReferralRewardCaseStatus.ClawbackPending
                && rewardCase.UpdatedAtUtc <= staleBefore
            )
            .OrderBy(rewardCase => rewardCase.UpdatedAtUtc)
            .Take(batchSize)
            .Select(rewardCase => new GrantableRewardCaseRef(rewardCase.TenantId, rewardCase.Id))
            .ToListAsync(ct);
    }

    private static readonly TimeSpan WalletResponseGrace = TimeSpan.FromMinutes(5);

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
