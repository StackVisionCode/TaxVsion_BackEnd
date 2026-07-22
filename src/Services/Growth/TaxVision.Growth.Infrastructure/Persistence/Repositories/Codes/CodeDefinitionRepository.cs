using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Codes;

public sealed class CodeDefinitionRepository(GrowthDbContext dbContext, ITenantContext tenantContext)
    : ICodeDefinitionRepository
{
    public Task<CodeDefinition?> GetOwnedByIdAsync(
        Guid ownerTenantId,
        Guid codeDefinitionId,
        CancellationToken ct = default
    )
    {
        if (codeDefinitionId == Guid.Empty || !TenantRepositoryGuard.Matches(tenantContext, ownerTenantId))
            return Task.FromResult<CodeDefinition?>(null);

        return dbContext
            .CodeDefinitions.Include(definition => definition.RuleVersions)
            .Include(definition => definition.Scopes)
            .FirstOrDefaultAsync(
                definition => definition.Id == codeDefinitionId && definition.TenantId == ownerTenantId,
                ct
            );
    }

    public Task<CodeDefinition?> GetApplicableByHashAsync(
        Guid consumingTenantId,
        CodeTokenHash codeHash,
        CancellationToken ct = default
    )
    {
        if (!TenantRepositoryGuard.Matches(tenantContext, consumingTenantId))
            return Task.FromResult<CodeDefinition?>(null);

        return ApplicableDefinitions(consumingTenantId)
            .Where(definition => definition.CodeHash == codeHash)
            .OrderByDescending(definition => definition.TenantId == consumingTenantId)
            .FirstOrDefaultAsync(ct);
    }

    public Task<CodeDefinition?> GetApplicableByIdAsync(
        Guid consumingTenantId,
        Guid codeDefinitionId,
        CancellationToken ct = default
    )
    {
        if (codeDefinitionId == Guid.Empty || !TenantRepositoryGuard.Matches(tenantContext, consumingTenantId))
            return Task.FromResult<CodeDefinition?>(null);

        return ApplicableDefinitions(consumingTenantId)
            .FirstOrDefaultAsync(definition => definition.Id == codeDefinitionId, ct);
    }

    public Task<CodeDefinition?> GetActiveBenefitGiftByTenantScopeAsync(
        Guid tenantScopeId,
        CancellationToken ct = default
    )
    {
        if (tenantScopeId == Guid.Empty || !TenantRepositoryGuard.Matches(tenantContext, tenantScopeId))
            return Task.FromResult<CodeDefinition?>(null);

        return dbContext
            .CodeDefinitions.IgnoreQueryFilters()
            .Include(definition => definition.RuleVersions)
            .Include(definition => definition.Scopes)
            .Where(definition =>
                definition.TenantScopeId == tenantScopeId
                && definition.Kind == CodeKind.BenefitGift
                && definition.Status == CodeDefinitionStatus.Active
            )
            .OrderByDescending(definition => definition.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public async Task AddAsync(CodeDefinition definition, CancellationToken ct = default)
    {
        TenantRepositoryGuard.EnsureMatches(tenantContext, definition.TenantId);
        await dbContext.CodeDefinitions.AddAsync(definition, ct);
    }

    private IQueryable<CodeDefinition> ApplicableDefinitions(Guid consumingTenantId) =>
        dbContext
            .CodeDefinitions.IgnoreQueryFilters()
            .Include(definition => definition.RuleVersions)
            .Include(definition => definition.Scopes)
            .Where(definition =>
                (definition.TenantId == consumingTenantId || definition.TenantId == PlatformTenant.Id)
                && (definition.TenantScopeId == null || definition.TenantScopeId == consumingTenantId)
            );
}
