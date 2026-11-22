using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Infrastructure.Persistence;

namespace TaxVision.Notification.Infrastructure.Retention;

public sealed class NotificationLogRetentionPurger(NotificationDbContext db)
{
    // Un DELETE por lote, sin traer filas: los intentos caen por la cascada de la FK.
    public async Task<int> PurgeAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
    {
        var total = 0;
        while (!ct.IsCancellationRequested)
        {
            var deleted = await db
                .NotificationLogs.IgnoreQueryFilters()
                .Where(log => log.CreatedAtUtc < cutoffUtc)
                .OrderBy(log => log.CreatedAtUtc)
                .Take(batchSize)
                .ExecuteDeleteAsync(ct);
            total += deleted;

            if (deleted < batchSize)
                return total;
        }

        return total;
    }
}
