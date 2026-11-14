using BuildingBlocks.Permissions;
using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaxVision.FinancialInsights.Domain.Projections;

namespace TaxVision.FinancialInsights.Infrastructure.Persistence;

public sealed class FinancialUserPermissionsReader(FinancialInsightsDbContext db)
    : IUserPermissionsProjectionReader, IUserPermissionsProjectionWriter
{
    public async Task<UserPermissionsSnapshot?> GetSnapshotAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var row = await db.UserPermissionsProjections.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId && x.IsActive && x.HasPermissionsSnapshot, ct);
        return row is null ? null : new UserPermissionsSnapshot(row.PermissionsVersion, row.PermissionCodes());
    }

    public async Task PersistSnapshotAsync(Guid tenantId, Guid userId, RemotePermissionsSnapshot snapshot,
        CancellationToken ct = default)
    {
        var row = await db.UserPermissionsProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, ct);
        if (row is null)
            db.UserPermissionsProjections.Add(UserPermissionsProjection.Create(tenantId, userId,
                snapshot.PermissionsVersion, snapshot.PermissionCodes, snapshot.RoleIds));
        else row.ApplyIfNewer(snapshot.PermissionsVersion, snapshot.PermissionCodes, snapshot.RoleIds);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            // Concurrent first requests can both pull the same Auth snapshot. The other request
            // already persisted it; this request authorizes from its own verified snapshot.
            foreach (var entry in ex.Entries) entry.State = EntityState.Detached;
        }
    }
}

public sealed class FinancialTenantModulesReader(FinancialInsightsDbContext db) : ITenantEntitlementModulesReader
{
    public async Task<IReadOnlyList<string>?> GetEnabledModulesAsync(Guid tenantId, CancellationToken ct = default)
    {
        var row = await db.TenantEntitlementsProjections.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        return row?.EnabledModules;
    }
}
