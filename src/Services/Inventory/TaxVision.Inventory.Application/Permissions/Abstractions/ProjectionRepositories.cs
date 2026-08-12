using TaxVision.Inventory.Domain.Permissions;

namespace TaxVision.Inventory.Application.Permissions.Abstractions;

public interface IUserPermissionsProjectionRepository
{
    Task<UserPermissionsProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
    Task AddAsync(UserPermissionsProjection projection, CancellationToken ct = default);
    Task<IReadOnlyList<UserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    );
}

public interface IRolePermissionsProjectionRepository
{
    Task<RolePermissionsProjection?> GetAsync(Guid tenantId, Guid roleId, CancellationToken ct = default);
    Task AddAsync(RolePermissionsProjection projection, CancellationToken ct = default);
    Task<IReadOnlyList<RolePermissionsProjection>> FindByRoleIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken ct = default
    );
}
