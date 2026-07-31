using TaxVision.Notification.Domain.Permissions;

namespace TaxVision.Notification.Application.Abstractions;

public interface INotificationRecipientRolePermissionsProjectionRepository
{
    Task<NotificationRecipientRolePermissionsProjection?> GetAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    );

    Task AddAsync(NotificationRecipientRolePermissionsProjection projection, CancellationToken ct = default);

    /// <summary>Cache de permisos de varios roles a la vez — usado para recomputar la unión de un usuario multi-rol.</summary>
    Task<IReadOnlyList<NotificationRecipientRolePermissionsProjection>> FindByRoleIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken ct = default
    );
}
