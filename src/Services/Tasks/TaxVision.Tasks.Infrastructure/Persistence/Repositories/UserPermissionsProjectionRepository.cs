using BuildingBlocks.Permissions;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Permissions.Abstractions;
using TaxVision.Tasks.Domain.Permissions;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

// Dos interfaces sobre la misma tabla: el puerto local que usan los consumers y el puerto angosto
// de BuildingBlocks que consulta ProjectionPermissionsSource. Una sola instancia scoped resuelve ambas.
public sealed class UserPermissionsProjectionRepository(TasksDbContext db)
    : IUserPermissionsProjectionRepository,
        IUserPermissionsProjectionReader
{
    // Corre en el scope de Wolverine, sin tenant en contexto: el tenantId viene del evento.
    public async Task<UserPermissionsProjection?> GetAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        await db
            .UserPermissionsProjections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId, ct);

    public async Task AddAsync(UserPermissionsProjection projection, CancellationToken ct = default) =>
        await db.UserPermissionsProjections.AddAsync(projection, ct);

    public async Task<IReadOnlyList<UserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    )
    {
        // RoleIds vive en JSON, así que el filtro por rol no es traducible a SQL: se filtra en memoria.
        var candidates = await db
            .UserPermissionsProjections.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.IsActive)
            .ToListAsync(ct);

        return candidates.Where(p => p.RoleIds().Contains(roleId)).ToList();
    }

    // Este corre dentro del request HTTP, con el tenant ya poblado: el filtro global aplica.
    public async Task<UserPermissionsSnapshot?> GetSnapshotAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    )
    {
        var projection = await db
            .UserPermissionsProjections.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId && p.IsActive, ct);

        return projection is null
            ? null
            : new UserPermissionsSnapshot(projection.PermissionsVersion, projection.PermissionCodes());
    }
}
