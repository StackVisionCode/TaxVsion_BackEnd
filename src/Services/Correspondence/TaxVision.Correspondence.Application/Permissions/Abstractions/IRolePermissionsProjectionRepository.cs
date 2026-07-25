using TaxVision.Correspondence.Domain.Permissions;

namespace TaxVision.Correspondence.Application.Abstractions;

public interface IRolePermissionsProjectionRepository
{
    Task<RolePermissionsProjection?> GetAsync(Guid tenantId, Guid roleId, CancellationToken ct = default);

    Task AddAsync(RolePermissionsProjection projection, CancellationToken ct = default);

    /// <summary>Cache de permisos de varios roles a la vez — usado para recomputar la unión de un usuario multi-rol.</summary>
    Task<IReadOnlyList<RolePermissionsProjection>> FindByRoleIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken ct = default
    );
}
