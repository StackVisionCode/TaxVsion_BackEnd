using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Permissions;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class UserPermissionsProjectionRepository(NotificationDbContext db) : IUserPermissionsProjectionRepository
{
    public async Task<UserPermissionsProjection?> GetAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) => await db.UserPermissionsProjections.FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId, ct);

    public async Task AddAsync(UserPermissionsProjection projection, CancellationToken ct = default) =>
        await db.UserPermissionsProjections.AddAsync(projection, ct);

    public async Task<IReadOnlyList<Guid>> FindActiveUserIdsByPermissionAsync(
        Guid tenantId,
        string permissionCode,
        CancellationToken ct = default
    )
    {
        // PermissionCodesJson no es indexable como columna nvarchar de SQL Server — se trae
        // la lista acotada por tenant+activo y se filtra el permiso puntual en memoria (mismo
        // trade-off aceptado en el resto de las tablas de este proyecto que usan la convención
        // de columna JSON-string para arrays, ver Invitation.RoleIdsJson en Auth).
        var candidates = await db
            .UserPermissionsProjections.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.IsActive)
            .ToListAsync(ct);

        return candidates
            .Where(p => p.PermissionCodes().Contains(permissionCode, StringComparer.OrdinalIgnoreCase))
            .Select(p => p.UserId)
            .ToList();
    }

    public async Task<IReadOnlyList<UserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    )
    {
        var candidates = await db
            .UserPermissionsProjections.Where(p => p.TenantId == tenantId && p.IsActive)
            .ToListAsync(ct);

        return candidates.Where(p => p.RoleIds().Contains(roleId)).ToList();
    }
}
