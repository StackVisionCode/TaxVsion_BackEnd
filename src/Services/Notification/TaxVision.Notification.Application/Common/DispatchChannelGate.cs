using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// <see cref="NotificationDeliveryGate"/> con las métricas del despacho: un usuario suspendido o retirado
/// no recibe nada por ningún canal y la preferencia apaga lo demás, aunque el consumer se olvide de comprobarlo.
/// </summary>
public sealed class DispatchChannelGate(NotificationDeliveryGate deliveryGate, INotificationDispatchMetrics metrics)
{
    /// <param name="activeUserId">Usuario que debe estar activo. In-app lo toma también del contexto.</param>
    /// <param name="preferenceUserId">Usuario cuya preferencia decide.</param>
    public async Task<bool> AllowsAsync(
        Guid tenantId,
        Guid? activeUserId,
        Guid? preferenceUserId,
        NotificationCategory category,
        NotificationChannel channel,
        string kind,
        CancellationToken ct
    )
    {
        if (await deliveryGate.IsRecipientInactiveAsync(tenantId, activeUserId, kind, channel, ct))
        {
            metrics.RecordNoRecipient(kind, channel.ToString());
            return false;
        }
        if (await deliveryGate.IsAllowedByPreferenceAsync(tenantId, preferenceUserId, category, channel, kind, ct))
            return true;

        metrics.RecordSuppressedByPreference(kind, channel.ToString());
        return false;
    }
}
