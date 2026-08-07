using TaxVision.Billing.Domain.Permissions;

namespace TaxVision.Billing.Application.Abstractions;

public interface IAuthzRolePermissionsProjectionRepository
{
    Task<AuthzRolePermissionsProjection?> GetAsync(Guid tenantId, Guid roleId, CancellationToken ct = default);

    Task AddAsync(AuthzRolePermissionsProjection projection, CancellationToken ct = default);

    /// <summary>Cache de permisos de varios roles a la vez — usado para recomputar la unión de un usuario multi-rol.</summary>
    Task<IReadOnlyList<AuthzRolePermissionsProjection>> FindByRoleIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken ct = default
    );
}
