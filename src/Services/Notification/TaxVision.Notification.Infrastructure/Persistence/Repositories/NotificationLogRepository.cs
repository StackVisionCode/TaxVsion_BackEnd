using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class NotificationLogRepository(NotificationDbContext db) : INotificationLogRepository
{
    public async Task AddAsync(NotificationLog log, CancellationToken ct = default) =>
        await db.NotificationLogs.AddAsync(log, ct);

    public async Task<(IReadOnlyList<NotificationLog> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId,
        NotificationStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = db.NotificationLogs.AsNoTracking().Where(log => log.TenantId == tenantId);

        if (status is not null)
            query = query.Where(log => log.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(log => log.CreatedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return (items, total);
    }
}
