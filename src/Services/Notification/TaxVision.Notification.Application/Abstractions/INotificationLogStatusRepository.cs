using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>Filas de auditoría con seguimiento, para corregir su estado con el resultado real de la entrega.</summary>
public interface INotificationLogStatusRepository
{
    /// <summary>Solo si la fila es de esa oficina.</summary>
    Task<NotificationLog?> FindAsync(Guid tenantId, Guid logId, CancellationToken ct = default);

    Task<NotificationLog?> FindByRelatedEventAsync(
        Guid tenantId,
        Guid relatedEventId,
        string kind,
        NotificationChannel channel,
        string recipient,
        CancellationToken ct = default
    );
}
