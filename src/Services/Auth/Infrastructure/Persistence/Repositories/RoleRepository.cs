using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Roles;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core del repositorio de roles: consultas de roles y permisos,
/// asignación de roles a usuarios y aprovisionamiento de los roles de sistema del tenant.
/// </summary>
public sealed class RoleRepository(AuthDbContext db) : IRoleRepository
{
    // IgnoreQueryFilters(): mismo bug que UserRepository.GetByIdAsync (ver su comentario) —
    // los 3 llamadores (Update/SetPermissions/DeactivateRole) ya validan
    // role.TenantId != command.TenantId post-fetch, así que el filtro ambiental era redundante.
    public Task<Role?> GetByIdAsync(Guid roleId, CancellationToken ct = default) =>
        db
            .Roles.IgnoreQueryFilters()
            .Include(role => role.Permissions)
            .FirstOrDefaultAsync(role => role.Id == roleId, ct);

    public async Task<IReadOnlyList<Role>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await db
            .Roles.IgnoreQueryFilters()
            .Include(role => role.Permissions)
            .Where(role => role.TenantId == tenantId)
            .OrderBy(role => role.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Role>> GetByIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken ct = default
    ) =>
        await db
            .Roles.IgnoreQueryFilters()
            .Include(role => role.Permissions)
            .Where(role => role.TenantId == tenantId && roleIds.Contains(role.Id))
            .ToListAsync(ct);

    public async Task AddAsync(Role role, CancellationToken ct = default) => await db.Roles.AddAsync(role, ct);

    public Task<bool> NameExistsAsync(Guid tenantId, string name, CancellationToken ct = default) =>
        db.Roles.IgnoreQueryFilters().AnyAsync(role => role.TenantId == tenantId && role.Name == name, ct);

    public Task<int> CountUsersInRoleAsync(Guid roleId, CancellationToken ct = default) =>
        db.UserRoles.CountAsync(link => link.RoleId == roleId, ct);

    public async Task<IReadOnlyList<Permission>> GetPermissionsCatalogAsync(CancellationToken ct = default) =>
        await db.Permissions.AsNoTracking().ToListAsync(ct);

    // IgnoreQueryFilters() (2026-08-06, Opción B): mismo bug que UserRepository.GetByIdAsync (ver
    // su comentario) — link.UserId ya es el límite real de autorización (un UserRole solo existe
    // para roles legítimamente asignados a ESE usuario, sin importar el tenant ambiental), así que
    // el HasQueryFilter de Role sobre ITenantContext era puramente redundante en el caso correcto y
    // activamente roto cuando el llamador corre en un scope sin ITenantContext poblado (handlers de
    // Wolverine invocados vía bus.InvokeAsync desde un controller, ver comentario de
    // UserRepository.GetByIdAsync) — devolvía 0 roles/permisos en silencio en vez de fallar. Hallado
    // implementando GetPermissionsSnapshotHandler (endpoint M2M interno), que hasta ahora era el
    // único llamador de estos dos métodos sin poder llamar tenantContext.SetTenant primero
    // (PermissionsBackfillService sí puede, por eso nunca lo disparó).
    public async Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
        await db
            .UserRoles.Where(link => link.UserId == userId)
            .Join(
                db.Roles.IgnoreQueryFilters().Include(role => role.Permissions),
                link => link.RoleId,
                role => role.Id,
                (link, role) => role
            )
            .ToListAsync(ct);

    /// <summary>Calcula los códigos de permiso efectivos del usuario combinando sus roles activos (sin duplicados).</summary>
    public async Task<IReadOnlyList<string>> GetEffectivePermissionCodesAsync(
        Guid userId,
        CancellationToken ct = default
    )
    {
        // Per-user deny layer: subtract the permissions this user is explicitly denied from the union of
        // their role permissions. Kept as a subquery so the subtraction runs in SQL (NOT IN), not in memory.
        // Un deny con fecha ya pasada no resta: sigue en la tabla como rastro, pero dejó de aplicar.
        var now = DateTime.UtcNow;
        var deniedPermissionIds = db
            .UserPermissionDenies.Where(deny =>
                deny.UserId == userId && (deny.ExpiresAtUtc == null || deny.ExpiresAtUtc > now)
            )
            .Select(deny => deny.PermissionId);

        return await db
            .UserRoles.Where(link => link.UserId == userId)
            .Join(
                db.Roles.IgnoreQueryFilters().Where(role => role.IsActive),
                link => link.RoleId,
                role => role.Id,
                (link, role) => role.Id
            )
            .Join(
                db.Set<RolePermission>(),
                roleId => roleId,
                rolePermission => rolePermission.RoleId,
                (roleId, rolePermission) => rolePermission.PermissionId
            )
            .Where(permissionId => !deniedPermissionIds.Contains(permissionId))
            .Join(
                db.Permissions,
                permissionId => permissionId,
                permission => permission.Id,
                (permissionId, permission) => permission.Code
            )
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>Reemplaza todas las asignaciones de rol del usuario por el conjunto indicado.</summary>
    public async Task ReplaceUserRolesAsync(
        Guid userId,
        IReadOnlyCollection<Guid> roleIds,
        Guid? assignedByUserId,
        CancellationToken ct = default
    )
    {
        var existing = await db.UserRoles.Where(link => link.UserId == userId).ToListAsync(ct);
        db.UserRoles.RemoveRange(existing);

        foreach (var roleId in roleIds.Distinct())
            await db.UserRoles.AddAsync(UserRole.Create(userId, roleId, assignedByUserId), ct);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Role>>> GetRolesByUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken ct = default
    )
    {
        if (userIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<Role>>();

        // Mismo criterio que GetUserRolesAsync: el UserId del link es el límite real, así que el
        // filtro ambiental de tenant sobre Role es redundante y rompe en scopes sin ITenantContext.
        var pairs = await db
            .UserRoles.Where(link => userIds.Contains(link.UserId))
            .Join(
                db.Roles.IgnoreQueryFilters().Include(role => role.Permissions),
                link => link.RoleId,
                role => role.Id,
                (link, role) => new { link.UserId, Role = role }
            )
            .ToListAsync(ct);

        return pairs
            .GroupBy(pair => pair.UserId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Role>)group.Select(pair => pair.Role).ToList());
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetDeniedPermissionIdsByUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken ct = default
    )
    {
        if (userIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<Guid>>();

        var now = DateTime.UtcNow;
        var rows = await db
            .UserPermissionDenies.Where(deny =>
                userIds.Contains(deny.UserId) && (deny.ExpiresAtUtc == null || deny.ExpiresAtUtc > now)
            )
            .Select(deny => new { deny.UserId, deny.PermissionId })
            .ToListAsync(ct);

        return rows.GroupBy(row => row.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Guid>)group.Select(row => row.PermissionId).ToList()
            );
    }

    public async Task<IReadOnlyList<Guid>> GetDeniedPermissionIdsAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await db
            .UserPermissionDenies.Where(deny =>
                deny.UserId == userId && (deny.ExpiresAtUtc == null || deny.ExpiresAtUtc > now)
            )
            .Select(deny => deny.PermissionId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<UserPermissionDeny>> GetActiveDeniesAsync(
        Guid userId,
        CancellationToken ct = default
    )
    {
        var now = DateTime.UtcNow;
        return await db
            .UserPermissionDenies.AsNoTracking()
            .Where(deny => deny.UserId == userId && (deny.ExpiresAtUtc == null || deny.ExpiresAtUtc > now))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<(Guid UserId, Guid TenantId)>> GetUsersWithExpiredDeniesAsync(
        DateTime nowUtc,
        int take,
        CancellationToken ct = default
    )
    {
        var rows = await db
            .UserPermissionDenies.Where(deny => deny.ExpiresAtUtc != null && deny.ExpiresAtUtc <= nowUtc)
            .Join(
                db.Users.IgnoreQueryFilters(),
                deny => deny.UserId,
                user => user.Id,
                (deny, user) => new { user.Id, user.TenantId }
            )
            .Distinct()
            .OrderBy(row => row.Id)
            .Take(take)
            .ToListAsync(ct);

        return rows.Select(row => (row.Id, row.TenantId)).ToList();
    }

    public async Task<int> RemoveExpiredDeniesAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default)
    {
        var expired = await db
            .UserPermissionDenies.Where(deny =>
                deny.UserId == userId && deny.ExpiresAtUtc != null && deny.ExpiresAtUtc <= nowUtc
            )
            .ToListAsync(ct);
        if (expired.Count == 0)
            return 0;

        db.UserPermissionDenies.RemoveRange(expired);
        return expired.Count;
    }

    /// <summary>Replaces the user's full deny set with the given permission ids. Mirrors
    /// <see cref="ReplaceUserRolesAsync"/>.</summary>
    public async Task ReplaceUserDeniesAsync(
        Guid userId,
        IReadOnlyCollection<PermissionDenyInput> denies,
        Guid? deniedByUserId,
        CancellationToken ct = default
    )
    {
        var existing = await db.UserPermissionDenies.Where(deny => deny.UserId == userId).ToListAsync(ct);
        db.UserPermissionDenies.RemoveRange(existing);

        foreach (var deny in denies.GroupBy(entry => entry.PermissionId).Select(group => group.First()))
        {
            await db.UserPermissionDenies.AddAsync(
                UserPermissionDeny.Create(userId, deny.PermissionId, deniedByUserId, deny.Reason, deny.ExpiresAtUtc),
                ct
            );
        }
    }

    /// <summary>Crea los roles de sistema del tenant (Admin, Empleado, Portal Cliente) que aún no existan, con sus permisos por defecto.</summary>
    public async Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken ct = default)
    {
        var systemNames = new[] { Role.SystemTenantAdmin, Role.SystemEmployee, Role.SystemCustomerPortal };

        var existingNames = await db
            .Roles.IgnoreQueryFilters()
            .Where(role => role.TenantId == tenantId && role.IsSystem)
            .Select(role => role.Name)
            .ToListAsync(ct);

        foreach (var name in systemNames.Except(existingNames, StringComparer.OrdinalIgnoreCase))
        {
            var roleResult = Role.Create(tenantId, name, "System role", isSystem: true);
            if (roleResult.IsFailure)
                continue;

            // 2026-08-06: el rol TenantAdmin de sistema usa el set que SÍ incluye IsDangerous
            // (roles.manage/billing.*/subscription.manage/tenant_domains.manage/
            // cloudstorage.legal.manage) — ver doc-comment de SystemTenantAdminRootPermissions.
            var permissionCodes =
                name == Role.SystemTenantAdmin
                    ? PermissionCatalog.SystemTenantAdminRootPermissions()
                    : PermissionCatalog.SystemRoleDefaults(name);
            var permissionIds = permissionCodes.Select(PermissionCatalog.IdOf).ToList();
            roleResult.Value.SetPermissions(permissionIds, seeding: true);
            await db.Roles.AddAsync(roleResult.Value, ct);
        }
    }

    public async Task EnsureSystemRolesCommittedAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (await GetSystemRoleAsync(tenantId, Role.SystemTenantAdmin, ct) is not null)
            return;

        await EnsureSystemRolesAsync(tenantId, ct);
        try
        {
            // Commitea SOLO los roles (nada más pendiente en este punto del flujo) para que una query
            // posterior los vea -- una consulta a DB no devuelve entidades Added sin persistir.
            await db.SaveChangesAsync(ct);
        }
        catch (ConflictException)
        {
            // Carrera con TenantCreatedConsumer (siembra async de roles): entre el chequeo de arriba y
            // este commit el otro camino sembró los mismos roles (unique IX_Roles_TenantId_Name). No es
            // error -- los roles YA existen, que es justo lo que este método garantiza. Se descartan los
            // insert en conflicto (siguen Added tras el fallo) para dejar el contexto limpio; el caller
            // los resolverá con GetSystemRoleAsync.
            foreach (
                var entry in db
                    .ChangeTracker.Entries()
                    .Where(e => e.State == EntityState.Added && e.Entity is Role or RolePermission)
                    .ToList()
            )
                entry.State = EntityState.Detached;
        }
    }

    public Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default) =>
        db
            .Roles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(role => role.TenantId == tenantId && role.IsSystem && role.Name == systemRoleName, ct);
}
