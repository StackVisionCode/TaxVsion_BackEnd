using TaxVision.Notification.Domain.Authorization;

namespace TaxVision.Notification.Application.Authorization.Abstractions;

public interface IAuthzUserPermissionsProjectionRepository
{
    Task<AuthzUserPermissionsProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    Task AddAsync(AuthzUserPermissionsProjection projection, CancellationToken ct = default);

    /// <summary>Usuarios activos del tenant que tienen <paramref name="roleId"/> entre sus roles — para recomputar la unión cuando cambia ese rol.</summary>
    Task<IReadOnlyList<AuthzUserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    );
}
