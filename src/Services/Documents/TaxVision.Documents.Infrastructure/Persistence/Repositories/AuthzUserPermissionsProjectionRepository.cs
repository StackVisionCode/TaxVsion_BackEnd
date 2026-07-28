using BuildingBlocks.Permissions;
using Microsoft.EntityFrameworkCore;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Domain.Permissions;

namespace TaxVision.Documents.Infrastructure.Persistence.Repositories;

/// <summary>
/// RBAC Fase 7: implementa DOS interfaces sobre la misma tabla — el puerto local rico
/// (IAuthzUserPermissionsProjectionRepository, para los consumers) y el puerto compartido angosto de
/// BuildingBlocks (IUserPermissionsProjectionReader.GetSnapshotAsync, lo único que necesita
/// ProjectionPermissionsSource para autorizar). Se registra una sola instancia scoped bajo ambas.
///
/// Todas las lecturas usan IgnoreQueryFilters() con tenantId explícito: los consumers y el source de
/// permisos corren en scopes de Wolverine/DI sin ITenantContext ambiental — el filtro global fail-closed
/// devolvería 0 filas. El tenantId ya viene explícito y confiable del evento/JWT.
/// </summary>
public sealed class AuthzUserPermissionsProjectionRepository(DocumentsDbContext db)
    : IAuthzUserPermissionsProjectionRepository,
        IUserPermissionsProjectionReader
{
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
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId && p.IsActive, ct);

        return projection is null
            ? null
            : new UserPermissionsSnapshot(projection.PermissionsVersion, projection.PermissionCodes());
    }
}
