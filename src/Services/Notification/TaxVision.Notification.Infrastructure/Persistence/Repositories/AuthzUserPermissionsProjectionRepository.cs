using BuildingBlocks.Permissions;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Authorization.Abstractions;
using TaxVision.Notification.Domain.Authorization;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

// RBAC Fase 7: esta clase implementa DOS interfaces con la misma tabla subyacente —
// el puerto local rico (IAuthzUserPermissionsProjectionRepository, usado por los consumers
// para escribir/leer la proyección) y el puerto compartido y angosto de BuildingBlocks
// (IUserPermissionsProjectionReader.GetSnapshotAsync, el único método que necesita
// ProjectionPermissionsSource para autorizar). Registradas como una sola instancia scoped
// resuelta bajo ambas interfaces (mismo patrón que CloudStorage/Customer), evitando dos
// lecturas separadas del mismo dato.
public sealed class AuthzUserPermissionsProjectionRepository(NotificationDbContext db)
    : IAuthzUserPermissionsProjectionRepository,
        IUserPermissionsProjectionReader
{
    // Bug real de producción (2026-07-22, mismo patrón que UserPermissionsProjectionRepository.cs
    // de Signature y FileObjectRepository.GetAsync de CloudStorage): este consumer Wolverine corre
    // sin TenantContext ambiente (no hay HTTP request), así que el filtro global de tenant de
    // NotificationDbContext tira antes de llegar acá. tenantId ya viene explícito y confiable
    // desde el evento — IgnoreQueryFilters() explícito.
    public async Task<AuthzUserPermissionsProjection?> GetAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        await db
            .AuthzUserPermissionsProjections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId, ct);

    public async Task AddAsync(AuthzUserPermissionsProjection projection, CancellationToken ct = default) =>
        await db.AuthzUserPermissionsProjections.AddAsync(projection, ct);

    public async Task<IReadOnlyList<AuthzUserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    )
    {
        var candidates = await db
            .AuthzUserPermissionsProjections.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.IsActive)
            .ToListAsync(ct);

        return candidates.Where(p => p.RoleIds().Contains(roleId)).ToList();
    }

    public async Task<UserPermissionsSnapshot?> GetSnapshotAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    )
    {
        var projection = await db
            .AuthzUserPermissionsProjections.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId && p.IsActive, ct);

        return projection is null
            ? null
            : new UserPermissionsSnapshot(projection.PermissionsVersion, projection.PermissionCodes());
    }
}
