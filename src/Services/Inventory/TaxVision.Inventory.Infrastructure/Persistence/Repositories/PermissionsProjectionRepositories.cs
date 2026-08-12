using BuildingBlocks.Permissions;
using Microsoft.EntityFrameworkCore;
using TaxVision.Inventory.Application.Permissions.Abstractions;
using TaxVision.Inventory.Domain.Permissions;

namespace TaxVision.Inventory.Infrastructure.Persistence.Repositories;

public sealed class UserPermissionsProjectionRepository(InventoryDbContext db)
    : IUserPermissionsProjectionRepository,
        IUserPermissionsProjectionReader
{
    public async Task<UserPermissionsProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        await db.UserPermissionsProjections.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId, ct);

    public async Task AddAsync(UserPermissionsProjection projection, CancellationToken ct = default) =>
        await db.UserPermissionsProjections.AddAsync(projection, ct);

    public async Task<IReadOnlyList<UserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
    {
        var candidates = await db.UserPermissionsProjections.IgnoreQueryFilters().Where(p => p.TenantId == tenantId && p.IsActive).ToListAsync(ct);
        return candidates.Where(p => p.RoleIds().Contains(roleId)).ToList();
    }

    public async Task<UserPermissionsSnapshot?> GetSnapshotAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var projection = await db.UserPermissionsProjections.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId && p.IsActive, ct);
        return projection is null ? null : new UserPermissionsSnapshot(projection.PermissionsVersion, projection.PermissionCodes());
    }
}

public sealed class RolePermissionsProjectionRepository(InventoryDbContext db) : IRolePermissionsProjectionRepository
{
    public async Task<RolePermissionsProjection?> GetAsync(Guid tenantId, Guid roleId, CancellationToken ct = default) =>
        await db.RolePermissionsProjections.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == roleId, ct);

    public async Task AddAsync(RolePermissionsProjection projection, CancellationToken ct = default) =>
        await db.RolePermissionsProjections.AddAsync(projection, ct);

    public async Task<IReadOnlyList<RolePermissionsProjection>> FindByRoleIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> roleIds, CancellationToken ct = default) =>
        await db.RolePermissionsProjections.IgnoreQueryFilters().Where(p => p.TenantId == tenantId && roleIds.Contains(p.Id)).ToListAsync(ct);
}
