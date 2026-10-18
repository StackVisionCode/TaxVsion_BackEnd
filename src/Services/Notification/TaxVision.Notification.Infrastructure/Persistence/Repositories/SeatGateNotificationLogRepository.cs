using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class SeatGateNotificationLogRepository(NotificationDbContext db) : ISeatGateNotificationLogRepository
{
    public Task<SeatGateNotificationLog?> GetOpenAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        db
            .SeatGateNotificationLogs.IgnoreQueryFilters()
            .Where(log => log.TenantId == tenantId && log.UserId == userId && log.ResolvedAtUtc == null)
            .OrderByDescending(log => log.NotifiedAtUtc)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(SeatGateNotificationLog log, CancellationToken ct = default) =>
        await db.SeatGateNotificationLogs.AddAsync(log, ct);
}
