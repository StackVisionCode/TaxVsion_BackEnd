using BuildingBlocks.Tenancy;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories;

internal static class TenantRepositoryGuard
{
    public static bool Matches(ITenantContext tenantContext, Guid tenantId) =>
        tenantId != Guid.Empty
        && tenantContext.HasTenant
        && tenantContext.TenantId == tenantId;

    public static void EnsureMatches(ITenantContext tenantContext, Guid tenantId)
    {
        if (!Matches(tenantContext, tenantId))
            throw new InvalidOperationException("The entity does not belong to the active tenant scope.");
    }
}
