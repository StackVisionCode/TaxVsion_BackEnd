using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;

public sealed class ReferralProgramRepository(
    GrowthDbContext dbContext,
    ITenantContext tenantContext
) : IReferralProgramRepository
{
    public Task<ReferralProgram?> GetOwnedByIdAsync(
        Guid ownerTenantId,
        Guid programId,
        CancellationToken ct = default
    ) =>
        programId == Guid.Empty
        || !TenantRepositoryGuard.Matches(tenantContext, ownerTenantId)
            ? Task.FromResult<ReferralProgram?>(null)
            : dbContext.ReferralPrograms.FirstOrDefaultAsync(
                program =>
                    program.Id == programId
                    && program.TenantId == ownerTenantId,
                ct
            );

    public Task<ReferralProgram?> GetForEvaluationAsync(
        Guid programId,
        CancellationToken ct = default
    )
    {
        if (!tenantContext.HasTenant || tenantContext.TenantId == Guid.Empty || programId == Guid.Empty)
            return Task.FromResult<ReferralProgram?>(null);

        var consumingTenantId = tenantContext.TenantId;

        // This is the only catalog elevation: one exact program ID, constrained to
        // either the platform catalog or the active tenant's catalog and scope.
        return dbContext
            .ReferralPrograms.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                program =>
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
                    ),
                ct
            );
    }

    public async Task AddAsync(
        ReferralProgram program,
        CancellationToken ct = default
    )
    {
        TenantRepositoryGuard.EnsureMatches(tenantContext, program.TenantId);
        await dbContext.ReferralPrograms.AddAsync(program, ct);
    }
}
