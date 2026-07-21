using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Abstractions;

public interface IUserNotificationPreferenceRepository
{
    /// <summary>Opt-out por defecto: sin fila ⇒ true (comportamiento actual, sin sorpresas para usuarios existentes).</summary>
    Task<bool> IsEnabledAsync(
        Guid tenantId,
        Guid userId,
        NotificationCategory category,
        NotificationChannel channel,
        CancellationToken ct = default
    );

    Task<UserNotificationPreference?> GetAsync(
        Guid tenantId,
        Guid userId,
        NotificationCategory category,
        NotificationChannel channel,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<UserNotificationPreference>> ListForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    );

    Task AddAsync(UserNotificationPreference preference, CancellationToken ct = default);
}
