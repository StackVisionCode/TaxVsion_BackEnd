using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Permissions;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class UserPermissionsProjectionRepository(NotificationDbContext db) : IUserPermissionsProjectionRepository
{
    // Bug real de producción (2026-07-22, mismo patrón que UserPermissionsProjectionRepository.cs
    // de Signature y FileObjectRepository.GetAsync de CloudStorage): este consumer Wolverine corre
    // sin TenantContext ambiente (no hay HTTP request), así que el filtro global de tenant de
    // NotificationDbContext tira antes de llegar acá. tenantId ya viene explícito y confiable
    // desde el evento — IgnoreQueryFilters() explícito.
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
        // Mismo bug de TenantContext ambiente que GetAsync arriba — RecipientResolver llama esto
        // desde el dispatch de notificaciones (sin HTTP request), IgnoreQueryFilters() explícito.
        var candidates = await db
            .UserPermissionsProjections.AsNoTracking()
            .IgnoreQueryFilters()
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
            .UserPermissionsProjections.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.IsActive)
            .ToListAsync(ct);

        return candidates.Where(p => p.RoleIds().Contains(roleId)).ToList();
    }
}
