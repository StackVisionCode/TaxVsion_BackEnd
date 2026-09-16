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

    /// <summary>The permission ids this user is explicitly denied (the per-user RBAC deny layer). The
    /// effective permission set is the union of the user's role permissions minus these.</summary>
    Task<IReadOnlyList<Guid>> GetDeniedPermissionIdsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Replaces the user's full deny set with the given permission ids (idempotent). Mirrors
    /// <see cref="ReplaceUserRolesAsync"/>.</summary>
    Task ReplaceUserDeniesAsync(
        Guid userId,
        IReadOnlyCollection<Guid> permissionIds,
        Guid? deniedByUserId,
        CancellationToken ct = default
    );

    /// <summary>Crea los roles de sistema del tenant si no existen (idempotente).</summary>
    Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Garantiza+commitea los roles de sistema del tenant tolerando una siembra concurrente
    /// (TenantCreatedConsumer): si el otro camino ya los creó entre el chequeo y el commit, el
    /// duplicate-key del índice único se trata como no-op (los roles ya existen).</summary>
    Task EnsureSystemRolesCommittedAsync(Guid tenantId, CancellationToken ct = default);

    Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default);
}
