using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Gate único de entrega por destinatario: lo usan el dispatcher (SMS, push, in-app) y el gateway de
/// correo. Primero el destinatario inactivo (no recibe nada por ningún canal), después la preferencia:
/// categoría bloqueada u obligatoria por catálogo pasan siempre.
/// </summary>
public sealed class NotificationDeliveryGate(
    IUserNotificationPreferenceRepository preferences,
    IUserEmailDirectoryRepository userDirectory,
    ILogger<NotificationDeliveryGate> logger
)
{
    public async Task<bool> IsAllowedAsync(
        Guid tenantId,
        Guid? recipientUserId,
        NotificationCategory category,
        NotificationChannel channel,
        string? templateKey,
        CancellationToken ct = default
    ) =>
        !await IsRecipientInactiveAsync(tenantId, recipientUserId, templateKey ?? string.Empty, channel, ct)
        && await IsAllowedByPreferenceAsync(tenantId, recipientUserId, category, channel, templateKey, ct);

    /// <summary>
    /// Destinatario suspendido o retirado según el directorio local (lo mantienen los eventos de
    /// ciclo de vida de Auth). Sin fila no hay señal de inactividad y se sigue: ausencia ≠ inactivo.
    /// </summary>
    public async Task<bool> IsRecipientInactiveAsync(
        Guid tenantId,
        Guid? recipientUserId,
        string kind,
        NotificationChannel channel,
        CancellationToken ct = default
    )
    {
        if (recipientUserId is not { } userId || userId == Guid.Empty)
            return false;

        var entry = await userDirectory.FindAsync(tenantId, userId, ct);
        if (entry is null || entry.IsActive)
            return false;

        logger.LogInformation(
            "Notification skipped: user {UserId} in tenant {TenantId} is inactive (channel {Channel}, kind {Kind}).",
            userId,
            tenantId,
            channel,
            kind
        );
        return true;
    }

    /// <summary>
    /// Cuenta y seguridad nunca se filtra (locked). Sin un UserId resuelto no hay preferencia que
    /// consultar y se permite. Cuando apaga un envío deja rastro en <c>Information</c>: es el resultado
    /// de una decisión del usuario y es lo que soporte necesita leer.
    /// </summary>
    public async Task<bool> IsAllowedByPreferenceAsync(
        Guid tenantId,
        Guid? recipientUserId,
        NotificationCategory category,
        NotificationChannel channel,
        string? templateKey,
        CancellationToken ct = default
    )
    {
        if (NotificationCategoryRules.IsLocked(category))
            return true;
        // D2: lo obligatorio por catálogo nunca lo apaga la preferencia del usuario.
        if (
            !string.IsNullOrWhiteSpace(templateKey)
            && NotificationCatalog.TryGet(templateKey, out var catalogEntry)
            && catalogEntry is not null
            && catalogEntry.IsMandatoryFor(channel)
        )
            return true;
        if (recipientUserId is null)
            return true;

        var enabled = await preferences.IsEnabledAsync(tenantId, recipientUserId.Value, category, channel, ct);
        if (!enabled)
        {
            logger.LogInformation(
                "Notification suppressed by user preference: tenant {TenantId}, user {UserId}, "
                    + "category {Category}, channel {Channel}.",
                tenantId,
                recipientUserId.Value,
                category,
                channel
            );
        }

        return enabled;
    }
}
