using TaxVision.Catalog.Application.Permissions.Abstractions;
using TaxVision.Catalog.Domain.Permissions;

namespace TaxVision.Catalog.Tests.Fakes;

internal sealed class FakeUserPermissionsProjectionRepository : IUserPermissionsProjectionRepository
{
    public List<UserPermissionsProjection> Store { get; } = [];

    public void Seed(UserPermissionsProjection p) => Store.Add(p);

    public Task<UserPermissionsProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult(Store.FirstOrDefault(p => p.TenantId == tenantId && p.UserId == userId));

    public Task AddAsync(UserPermissionsProjection projection, CancellationToken ct = default)
    {
        Store.Add(projection);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    )
    {
        IReadOnlyList<UserPermissionsProjection> result = Store
            .Where(p => p.TenantId == tenantId && p.RoleIds().Contains(roleId))
            .ToList();
        return Task.FromResult(result);
    }
}

internal sealed class FakeRolePermissionsProjectionRepository : IRolePermissionsProjectionRepository
{
    public List<RolePermissionsProjection> Store { get; } = [];

    public void Seed(RolePermissionsProjection p) => Store.Add(p);

    public Task<RolePermissionsProjection?> GetAsync(Guid tenantId, Guid roleId, CancellationToken ct = default) =>
        Task.FromResult(Store.FirstOrDefault(p => p.TenantId == tenantId && p.Id == roleId));

    public Task AddAsync(RolePermissionsProjection projection, CancellationToken ct = default)
    {
        Store.Add(projection);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RolePermissionsProjection>> FindByRoleIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken ct = default
    )
    {
        IReadOnlyList<RolePermissionsProjection> result = Store
            .Where(p => p.TenantId == tenantId && roleIds.Contains(p.Id))
            .ToList();
        return Task.FromResult(result);
    }
}
