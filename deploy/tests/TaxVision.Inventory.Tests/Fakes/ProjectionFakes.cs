using TaxVision.Inventory.Application.Permissions.Abstractions;
using TaxVision.Inventory.Domain.Permissions;

namespace TaxVision.Inventory.Tests.Fakes;

internal sealed class FakeUserPermissionsProjectionRepository : IUserPermissionsProjectionRepository
{
    public List<UserPermissionsProjection> Store { get; } = [];
    public void Seed(UserPermissionsProjection p) => Store.Add(p);
    public Task<UserPermissionsProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult(Store.FirstOrDefault(p => p.TenantId == tenantId && p.UserId == userId));
    public Task AddAsync(UserPermissionsProjection projection, CancellationToken ct = default) { Store.Add(projection); return Task.CompletedTask; }
    public Task<IReadOnlyList<UserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
    {
        IReadOnlyList<UserPermissionsProjection> r = Store.Where(p => p.TenantId == tenantId && p.RoleIds().Contains(roleId)).ToList();
        return Task.FromResult(r);
    }
}

internal sealed class FakeRolePermissionsProjectionRepository : IRolePermissionsProjectionRepository
{
    public List<RolePermissionsProjection> Store { get; } = [];
    public void Seed(RolePermissionsProjection p) => Store.Add(p);
    public Task<RolePermissionsProjection?> GetAsync(Guid tenantId, Guid roleId, CancellationToken ct = default) =>
        Task.FromResult(Store.FirstOrDefault(p => p.TenantId == tenantId && p.Id == roleId));
    public Task AddAsync(RolePermissionsProjection projection, CancellationToken ct = default) { Store.Add(projection); return Task.CompletedTask; }
    public Task<IReadOnlyList<RolePermissionsProjection>> FindByRoleIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> roleIds, CancellationToken ct = default)
    {
        IReadOnlyList<RolePermissionsProjection> r = Store.Where(p => p.TenantId == tenantId && roleIds.Contains(p.Id)).ToList();
        return Task.FromResult(r);
    }
}
