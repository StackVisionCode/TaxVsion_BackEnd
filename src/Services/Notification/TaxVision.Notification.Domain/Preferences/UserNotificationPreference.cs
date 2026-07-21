using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Domain.Preferences;

/// <summary>
/// Interruptor por categoría+canal. Fase 5 del plan de notificaciones dinámicas — la versión
/// anterior de esta tabla se borró (ver Hardening Fase 20, 2026-07-18) porque ningún consumer
/// la consultaba; esta vez <see cref="TaxVision.Notification.Application.Common.NotificationDispatcher"/>
/// la consulta siempre (parámetro obligatorio, no opcional) en vez de depender de que cada
/// consumer se acuerde de llamarla.
/// </summary>
public sealed class UserNotificationPreference : TenantEntity
{
    private UserNotificationPreference() { }

    public Guid UserId { get; private set; }
    public NotificationCategory Category { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public bool Enabled { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Opt-out, no opt-in: solo existe una fila si el usuario cambió el default (Enabled=true).</summary>
    public static Result<UserNotificationPreference> Create(
        Guid tenantId,
        Guid userId,
        NotificationCategory category,
        NotificationChannel channel,
        bool enabled
    )
    {
        if (NotificationCategoryRules.IsLocked(category))
        {
            return Result.Failure<UserNotificationPreference>(
                new Error("UserNotificationPreference.CategoryLocked", $"{category} cannot be disabled.")
            );
        }

        var preference = new UserNotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Category = category,
            Channel = channel,
            Enabled = enabled,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        preference.SetTenant(tenantId);
        return Result.Success(preference);
    }

    public void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return;
        Enabled = enabled;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
