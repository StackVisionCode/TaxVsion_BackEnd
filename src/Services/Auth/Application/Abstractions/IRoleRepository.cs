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

    /// <summary>The permission ids this user is explicitly denied and that todavía aplican (los vencidos
    /// no restan). The effective permission set is the union of the user's role permissions minus these.</summary>
    Task<IReadOnlyList<Guid>> GetDeniedPermissionIdsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Los denies vigentes del usuario con su razón y su expiración, para mostrarlos en la UI.</summary>
    Task<IReadOnlyList<UserPermissionDeny>> GetActiveDeniesAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<UserPermissionDeny>>([]);

    /// <summary>Replaces the user's full deny set with the given entries (idempotent). Mirrors
    /// <see cref="ReplaceUserRolesAsync"/>.</summary>
    Task ReplaceUserDeniesAsync(
        Guid userId,
        IReadOnlyCollection<PermissionDenyInput> denies,
        Guid? deniedByUserId,
        CancellationToken ct = default
    );

    /// <summary>Los denies vencidos de todos los tenants, para que el job los limpie.</summary>
    Task<IReadOnlyList<(Guid UserId, Guid TenantId)>> GetUsersWithExpiredDeniesAsync(
        DateTime nowUtc,
        int take,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<(Guid, Guid)>>([]);

    /// <summary>Borra los denies vencidos del usuario y devuelve cuántos quitó.</summary>
    Task<int> RemoveExpiredDeniesAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default) =>
        Task.FromResult(0);

    /// <summary>Los roles de cada uno de estos usuarios, en una sola consulta — el fan-out por
    /// titular necesita la unión completa de cada uno, no solo el rol que cambió.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Role>>> GetRolesByUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Role>>>(new Dictionary<Guid, IReadOnlyList<Role>>());

    /// <summary>Los denies de cada uno de estos usuarios, en una sola consulta.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetDeniedPermissionIdsByUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(new Dictionary<Guid, IReadOnlyList<Guid>>());

    /// <summary>Crea los roles de sistema del tenant si no existen (idempotente).</summary>
    Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Garantiza+commitea los roles de sistema del tenant tolerando una siembra concurrente
    /// (TenantCreatedConsumer): si el otro camino ya los creó entre el chequeo y el commit, el
    /// duplicate-key del índice único se trata como no-op (los roles ya existen).</summary>
    Task EnsureSystemRolesCommittedAsync(Guid tenantId, CancellationToken ct = default);

    Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default);
}
