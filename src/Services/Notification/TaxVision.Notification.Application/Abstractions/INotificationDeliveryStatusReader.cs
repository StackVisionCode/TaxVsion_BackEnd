using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>Estado del último correo de un evento en una oficina (AJ-08). Null si no hay correo de ese evento.</summary>
public interface INotificationDeliveryStatusReader
{
    Task<NotificationStatus?> GetEmailStatusByEventAsync(
        Guid tenantId,
        Guid relatedEventId,
        CancellationToken ct = default
    );
}
