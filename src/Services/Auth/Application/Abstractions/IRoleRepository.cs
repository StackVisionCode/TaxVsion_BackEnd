using TaxVision.Auth.Domain.Roles;

namespace TaxVision.Auth.Application.Abstractions;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(Guid roleId, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetByIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken ct = default
    );
    Task AddAsync(Role role, CancellationToken ct = default);
    Task<bool> NameExistsAsync(Guid tenantId, string name, CancellationToken ct = default);
    Task<int> CountUsersInRoleAsync(Guid roleId, CancellationToken ct = default);

    Task<IReadOnlyList<Permission>> GetPermissionsCatalogAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetEffectivePermissionCodesAsync(Guid userId, CancellationToken ct = default);
    Task ReplaceUserRolesAsync(
        Guid userId,
        IReadOnlyCollection<Guid> roleIds,
        Guid? assignedByUserId,
        CancellationToken ct = default
    );

    /// <summary>Crea los roles de sistema del tenant si no existen (idempotente).</summary>
    Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken ct = default);

    Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default);
}
