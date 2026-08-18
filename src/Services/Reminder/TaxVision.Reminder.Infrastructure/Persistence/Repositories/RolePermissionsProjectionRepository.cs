using Microsoft.EntityFrameworkCore;
using TaxVision.Reminder.Application.Permissions.Abstractions;
using TaxVision.Reminder.Domain.Permissions;

namespace TaxVision.Reminder.Infrastructure.Persistence.Repositories;

public sealed class RolePermissionsProjectionRepository(ReminderDbContext db) : IRolePermissionsProjectionRepository
{
    // Consumer Wolverine sin TenantContext ambiente (no hay HTTP request) — el filtro global de
    // tenant tiraría antes de llegar acá. tenantId ya viene explícito y confiable desde el evento.
    public async Task<RolePermissionsProjection?> GetAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    ) =>
        await db
            .RolePermissionsProjections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == roleId, ct);

    public async Task AddAsync(RolePermissionsProjection projection, CancellationToken ct = default) =>
        await db.RolePermissionsProjections.AddAsync(projection, ct);

    public async Task<IReadOnlyList<RolePermissionsProjection>> FindByRoleIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken ct = default
    ) =>
        await db
            .RolePermissionsProjections.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && roleIds.Contains(p.Id))
            .ToListAsync(ct);
}
