using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Permissions;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class NotificationRecipientPermissionsProjectionRepository(NotificationDbContext db)
    : INotificationRecipientPermissionsProjectionRepository
{
    // Consumer Wolverine sin TenantContext ambiente (no hay HTTP request) — el filtro global de
    // tenant de NotificationDbContext tiraría antes de llegar acá. tenantId ya viene explícito y
    // confiable desde el evento — IgnoreQueryFilters() explícito.
    public async Task<NotificationRecipientPermissionsProjection?> GetAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        await db
            .NotificationRecipientPermissionsProjections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId, ct);

    public async Task AddAsync(NotificationRecipientPermissionsProjection projection, CancellationToken ct = default) =>
        await db.NotificationRecipientPermissionsProjections.AddAsync(projection, ct);

    public async Task<IReadOnlyList<Guid>> FindActiveUserIdsByPermissionAsync(
        Guid tenantId,
        string permissionCode,
        CancellationToken ct = default
    )
    {
        // PermissionCodesJson no es indexable como columna nvarchar de SQL Server — se trae
        // la lista acotada por tenant+activo y se filtra el permiso puntual en memoria.
        // RecipientResolver llama esto desde el dispatch de notificaciones (sin HTTP request),
        // sin TenantContext ambiente — IgnoreQueryFilters() explícito.
        var candidates = await db
            .NotificationRecipientPermissionsProjections.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.IsActive)
            .ToListAsync(ct);

        return candidates
            .Where(p => p.PermissionCodes().Contains(permissionCode, StringComparer.OrdinalIgnoreCase))
            .Select(p => p.UserId)
            .ToList();
    }

    public async Task<IReadOnlyList<NotificationRecipientPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    )
    {
        var candidates = await db
            .NotificationRecipientPermissionsProjections.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.IsActive)
            .ToListAsync(ct);

        return candidates.Where(p => p.RoleIds().Contains(roleId)).ToList();
    }
}
