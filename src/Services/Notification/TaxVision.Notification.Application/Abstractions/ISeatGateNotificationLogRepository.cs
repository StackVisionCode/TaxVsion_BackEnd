using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Abstractions;

public interface ISeatGateNotificationLogRepository
{
    Task<SeatGateNotificationLog?> GetOpenAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
    Task AddAsync(SeatGateNotificationLog log, CancellationToken ct = default);
}
