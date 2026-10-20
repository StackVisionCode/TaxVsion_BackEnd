using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Infrastructure.Persistence;

namespace TaxVision.Notification.Infrastructure.Retention;

public sealed class NotificationLogRetentionPurger(NotificationDbContext db)
{
    public async Task<int> PurgeAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
    {
        var total = 0;
        while (!ct.IsCancellationRequested)
        {
            var logs = await db
                .NotificationLogs.IgnoreQueryFilters()
                .Include(log => log.Attempts)
                .Where(log => log.CreatedAtUtc < cutoffUtc)
                .OrderBy(log => log.CreatedAtUtc)
                .Take(batchSize)
                .ToListAsync(ct);

            if (logs.Count == 0)
                return total;

            db.NotificationLogs.RemoveRange(logs);
            await db.SaveChangesAsync(ct);
            total += logs.Count;
            db.ChangeTracker.Clear();

            if (logs.Count < batchSize)
                return total;
        }

        return total;
    }
}
