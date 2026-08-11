using BuildingBlocks.Permissions;
using Microsoft.EntityFrameworkCore;
using TaxVision.Sms.Application.Permissions.Abstractions;
using TaxVision.Sms.Domain.Permissions;

namespace TaxVision.Sms.Infrastructure.Persistence.Repositories;

// RBAC Fase 7: esta clase implementa DOS interfaces con la misma tabla subyacente — el puerto
// local rico (IUserPermissionsProjectionRepository, usado por los consumers para escribir/leer la
// proyección) y el puerto compartido y angosto de BuildingBlocks
// (IUserPermissionsProjectionReader.GetSnapshotAsync, el único método que necesita
// ProjectionPermissionsSource para autorizar). Registradas como una sola instancia scoped resuelta
// bajo ambas interfaces, evitando dos lecturas separadas del mismo dato.
public sealed class UserPermissionsProjectionRepository(SmsDbContext db)
    : IUserPermissionsProjectionRepository,
        IUserPermissionsProjectionReader
{
    // Consumer Wolverine sin TenantContext ambiente (no hay HTTP request) — el filtro global de
    // tenant de SmsDbContext tiraría antes de llegar acá. tenantId ya viene explícito y confiable
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

    // IgnoreQueryFilters: aunque ProjectionPermissionsSource corre en el scope de la request HTTP,
    // el tenantId ya llega explícito y validado desde el ClaimsPrincipal — mismo patrón defensivo
    // que el resto de los métodos en vez de depender del filtro global para el aislamiento.
    public async Task<UserPermissionsSnapshot?> GetSnapshotAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    )
    {
        var projection = await db
            .UserPermissionsProjections.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId && p.IsActive, ct);

        return projection is null
            ? null
            : new UserPermissionsSnapshot(projection.PermissionsVersion, projection.PermissionCodes());
    }
}
