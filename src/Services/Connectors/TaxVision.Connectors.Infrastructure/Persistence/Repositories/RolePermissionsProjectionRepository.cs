using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Domain.Permissions;
using TaxVision.Connectors.Infrastructure.Persistence;

namespace TaxVision.Connectors.Infrastructure.Persistence.Repositories;

public sealed class RolePermissionsProjectionRepository(ConnectorsDbContext db) : IRolePermissionsProjectionRepository
{
    // Bug real de producción (2026-07-22, mismo patrón que Signature's UserPermissionsProjectionRepository.cs
    // y CloudStorage's FileObjectRepository.GetAsync ese mismo día): este consumer Wolverine corre sin
    // TenantContext ambiente (no hay HTTP request), así que el filtro global de tenant de ConnectorsDbContext
    // tira antes de llegar acá. tenantId ya viene explícito y confiable desde el evento — IgnoreQueryFilters() explícito.
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
