using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Permissions;

namespace TaxVision.Billing.Infrastructure.Persistence.Repositories;

/// <summary>Cache de permisos por rol. IgnoreQueryFilters() + tenantId explícito (mismo motivo que el
/// repo de usuario: corre en scopes de Wolverine sin tenant ambiental).</summary>
public sealed class AuthzRolePermissionsProjectionRepository(BillingDbContext db)
    : IAuthzRolePermissionsProjectionRepository
{
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
