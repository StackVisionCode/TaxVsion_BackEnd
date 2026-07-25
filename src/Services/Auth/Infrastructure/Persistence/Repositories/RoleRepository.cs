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

    public async Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
        await db
            .UserRoles.Where(link => link.UserId == userId)
            .Join(
                db.Roles.Include(role => role.Permissions),
                link => link.RoleId,
                role => role.Id,
                (link, role) => role
            )
            .ToListAsync(ct);

    /// <summary>Calcula los códigos de permiso efectivos del usuario combinando sus roles activos (sin duplicados).</summary>
    public async Task<IReadOnlyList<string>> GetEffectivePermissionCodesAsync(
        Guid userId,
        CancellationToken ct = default
    ) =>
        await db
            .UserRoles.Where(link => link.UserId == userId)
            .Join(db.Roles.Where(role => role.IsActive), link => link.RoleId, role => role.Id, (link, role) => role.Id)
            .Join(
                db.Set<RolePermission>(),
                roleId => roleId,
                rolePermission => rolePermission.RoleId,
                (roleId, rolePermission) => rolePermission.PermissionId
            )
            .Join(
                db.Permissions,
                permissionId => permissionId,
                permission => permission.Id,
                (permissionId, permission) => permission.Code
            )
            .Distinct()
            .ToListAsync(ct);

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

            var permissionIds = PermissionCatalog.SystemRoleDefaults(name).Select(PermissionCatalog.IdOf).ToList();
            roleResult.Value.SetPermissions(permissionIds, seeding: true);
            await db.Roles.AddAsync(roleResult.Value, ct);
        }
    }

    public Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default) =>
        db
            .Roles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(role => role.TenantId == tenantId && role.IsSystem && role.Name == systemRoleName, ct);
}
