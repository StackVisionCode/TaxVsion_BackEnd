using TaxVision.Postmaster.Domain.Permissions;

namespace TaxVision.Postmaster.Application.Abstractions;

public interface IUserPermissionsProjectionRepository
{
    Task<UserPermissionsProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    Task AddAsync(UserPermissionsProjection projection, CancellationToken ct = default);

    /// <summary>Usuarios activos del tenant que tienen <paramref name="roleId"/> entre sus roles — para recomputar la unión cuando cambia ese rol.</summary>
    Task<IReadOnlyList<UserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    );
}
