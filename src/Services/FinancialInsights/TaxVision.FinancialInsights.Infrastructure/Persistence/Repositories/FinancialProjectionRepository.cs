using Microsoft.EntityFrameworkCore;
using TaxVision.FinancialInsights.Application.Projections.Abstractions;
using TaxVision.FinancialInsights.Domain.Projections;

namespace TaxVision.FinancialInsights.Infrastructure.Persistence.Repositories;

// Filas con seguimiento para que el consumer las modifique; tenant siempre explícito.
public sealed class FinancialProjectionRepository(FinancialInsightsDbContext db) : IFinancialProjectionRepository
{
    public Task<UserPermissionsProjection?> FindUserPermissionsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        db.UserPermissionsProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, ct);

    public void AddUserPermissions(UserPermissionsProjection projection) => db.UserPermissionsProjections.Add(projection);

    public Task<RolePermissionsProjection?> FindRolePermissionsAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    ) =>
        db.RolePermissionsProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == roleId, ct);

    public void AddRolePermissions(RolePermissionsProjection projection) => db.RolePermissionsProjections.Add(projection);

    public Task<TenantEntitlementsProjection?> FindTenantEntitlementsAsync(Guid tenantId, CancellationToken ct = default) =>
        db.TenantEntitlementsProjections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);

    public void AddTenantEntitlements(TenantEntitlementsProjection projection) =>
        db.TenantEntitlementsProjections.Add(projection);

    public async Task<bool> TryAddTenantEntitlementsAsync(
        TenantEntitlementsProjection projection,
        CancellationToken ct = default
    )
    {
        db.TenantEntitlementsProjections.Add(projection);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (FinancialInsightsDbContext.IsUniqueViolation(ex))
        {
            db.Entry(projection).State = EntityState.Detached;
            return false;
        }
    }

    public Task<TenantDirectoryProjection?> FindTenantDirectoryAsync(Guid tenantId, CancellationToken ct = default) =>
        db.TenantDirectoryProjections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);

    public void AddTenantDirectory(TenantDirectoryProjection projection) => db.TenantDirectoryProjections.Add(projection);

    public Task<CustomerDirectoryProjection?> FindCustomerAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) =>
        db.CustomerDirectoryProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.CustomerId == customerId, ct);

    public async Task<IReadOnlyCollection<Guid>> FindKnownCustomerIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken ct = default
    ) =>
        await db
            .CustomerDirectoryProjections.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && customerIds.Contains(x.CustomerId))
            .Select(x => x.CustomerId)
            .ToListAsync(ct);

    public void AddCustomer(CustomerDirectoryProjection projection) => db.CustomerDirectoryProjections.Add(projection);
}
