using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Permissions;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class NotificationRecipientRolePermissionsProjectionRepository(NotificationDbContext db)
    : INotificationRecipientRolePermissionsProjectionRepository
{
    // Consumer Wolverine sin TenantContext ambiente (no hay HTTP request) — el filtro global de
    // tenant de NotificationDbContext tiraría antes de llegar acá. tenantId ya viene explícito y
    // confiable desde el evento — IgnoreQueryFilters() explícito.
    public async Task<NotificationRecipientRolePermissionsProjection?> GetAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    ) =>
        await db
            .NotificationRecipientRolePermissionsProjections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == roleId, ct);

    public async Task AddAsync(
        NotificationRecipientRolePermissionsProjection projection,
        CancellationToken ct = default
    ) => await db.NotificationRecipientRolePermissionsProjections.AddAsync(projection, ct);

    public async Task<IReadOnlyList<NotificationRecipientRolePermissionsProjection>> FindByRoleIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken ct = default
    ) =>
        await db
            .NotificationRecipientRolePermissionsProjections.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && roleIds.Contains(p.Id))
            .ToListAsync(ct);
}
