using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Permissions;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

public sealed class AuthzRolePermissionsProjectionRepository(SignatureDbContext db)
    : IAuthzRolePermissionsProjectionRepository
{
    // Bug real de producción (2026-07-22, mismo patrón que UserPermissionsProjectionRepository.cs
    // de Signature y FileObjectRepository.GetAsync de CloudStorage): este consumer Wolverine corre
    // sin TenantContext ambiente (no hay HTTP request), así que el filtro global de tenant de
    // SignatureDbContext tira antes de llegar acá. tenantId ya viene explícito y confiable desde
    // el evento — IgnoreQueryFilters() explícito.
    public async Task<AuthzRolePermissionsProjection?> GetAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    ) =>
        await db
            .AuthzRolePermissionsProjections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == roleId, ct);

    public async Task AddAsync(AuthzRolePermissionsProjection projection, CancellationToken ct = default) =>
        await db.AuthzRolePermissionsProjections.AddAsync(projection, ct);

    public async Task<IReadOnlyList<AuthzRolePermissionsProjection>> FindByRoleIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken ct = default
    ) =>
        await db
            .AuthzRolePermissionsProjections.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && roleIds.Contains(p.Id))
            .ToListAsync(ct);
}
