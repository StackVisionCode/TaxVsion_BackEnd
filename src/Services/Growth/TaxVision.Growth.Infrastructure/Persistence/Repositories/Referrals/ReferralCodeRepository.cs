using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;

public sealed class ReferralCodeRepository(GrowthDbContext dbContext, ITenantContext tenantContext)
    : IReferralCodeRepository
{
    public Task<ReferralCode?> GetActiveOwnedAsync(
        Guid ownerTenantId,
        Guid programId,
        ReferralParticipantType ownerType,
        Guid ownerId,
        CancellationToken ct = default
    ) =>
        programId == Guid.Empty || ownerId == Guid.Empty || !TenantRepositoryGuard.Matches(tenantContext, ownerTenantId)
            ? Task.FromResult<ReferralCode?>(null)
            : dbContext
                .ReferralCodes.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    code =>
                        code.TenantId == ownerTenantId
                        && code.ProgramId == programId
                        && code.OwnerType == ownerType
                        && code.OwnerId == ownerId
                        && code.Status == ReferralCodeStatus.Active,
                    ct
                );

    public Task<ReferralCode?> ResolveByHashAsync(Guid programId, string codeHash, CancellationToken ct = default)
    {
        if (
            !tenantContext.HasTenant
            || tenantContext.TenantId == Guid.Empty
            || programId == Guid.Empty
            || !IsSha256Hex(codeHash)
        )
            return Task.FromResult<ReferralCode?>(null);

        var consumingTenantId = tenantContext.TenantId;
        var normalizedHash = codeHash.Trim().ToLowerInvariant();

        // A T2T referral code belongs to the referrer tenant but is redeemed under
        // the referee tenant. Elevation is therefore limited to one ProgramId+hash
        // and only when that program is applicable to the active tenant.
        return dbContext
            .ReferralCodes.IgnoreQueryFilters()
            .Where(code =>
                code.ProgramId == programId
                && code.CodeHash == normalizedHash
                && (code.TenantScopeId == null || code.TenantScopeId == consumingTenantId)
            )
            .Where(_ =>
                dbContext
                    .ReferralPrograms.IgnoreQueryFilters()
                    .Any(program =>
                        program.Id == programId
                        && (
                            (
                                program.TenantId == PlatformTenant.Id
                                && program.ScopeType == ReferralProgramScope.Platform
                                && program.TenantScopeId == null
                            )
                            || (
                                program.TenantId == consumingTenantId
                                && program.ScopeType == ReferralProgramScope.Tenant
                                && program.TenantScopeId == consumingTenantId
                            )
                        )
                    )
            )
            .SingleOrDefaultAsync(ct);
    }

    public async Task AddAsync(ReferralCode referralCode, CancellationToken ct = default)
    {
        TenantRepositoryGuard.EnsureMatches(tenantContext, referralCode.TenantId);
        await dbContext.ReferralCodes.AddAsync(referralCode, ct);
    }

    private static bool IsSha256Hex(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length == 64 && value.Trim().All(Uri.IsHexDigit);
}
