using TaxVision.Notification.Domain.Permissions;

namespace TaxVision.Notification.Application.Abstractions;

public interface INotificationRecipientPermissionsProjectionRepository
{
    Task<NotificationRecipientPermissionsProjection?> GetAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    );

    Task AddAsync(NotificationRecipientPermissionsProjection projection, CancellationToken ct = default);

    /// <summary>Usuarios activos del tenant cuyo conjunto de permisos efectivos incluye <paramref name="permissionCode"/> — resuelve <c>ByPermission</c>.</summary>
    Task<IReadOnlyList<Guid>> FindActiveUserIdsByPermissionAsync(
        Guid tenantId,
        string permissionCode,
        CancellationToken ct = default
    );

    /// <summary>
    /// De <paramref name="userIds"/>, los que están activos y (si <paramref name="requiredCapability"/> no es null)
    /// tienen esa capacidad — valida destinatarios ya conocidos (Participants/Mailbox/Explicit) por T4.2.
    /// Un usuario sin fila de proyección no pasa cuando se exige capacidad (fail-closed para el gate de capacidad).
    /// </summary>
    Task<IReadOnlyList<Guid>> FilterActiveWithCapabilityAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> userIds,
        string? requiredCapability,
        CancellationToken ct = default
    );

    /// <summary>Usuarios activos del tenant que tienen <paramref name="roleId"/> entre sus roles — para recomputar la unión cuando cambia ese rol.</summary>
    Task<IReadOnlyList<NotificationRecipientPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    );
}
