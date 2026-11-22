using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Infrastructure.Persistence;

namespace TaxVision.Notification.Infrastructure.Retention;

/// <summary>Red de seguridad de los tokens de push (D-N37): apaga los dormidos y borra los revocados viejos.</summary>
public sealed class PushDeviceTokenRetentionPurger(NotificationDbContext db)
{
    // Activos sin alta reciente (sin LastSeenAtUtc cuenta el registro): quedan revocados con fecha de hoy.
    public async Task<int> DeactivateIdleAsync(
        DateTime idleBeforeUtc,
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct
    )
    {
        var total = 0;
        while (!ct.IsCancellationRequested)
        {
            var updated = await db
                .PushDeviceTokens.IgnoreQueryFilters()
                .Where(device => device.IsActive && (device.LastSeenAtUtc ?? device.RegisteredAtUtc) < idleBeforeUtc)
                .OrderBy(device => device.Id)
                .Take(batchSize)
                .ExecuteUpdateAsync(
                    set =>
                        set.SetProperty(device => device.IsActive, false)
                            .SetProperty(device => device.RevokedAtUtc, nowUtc),
                    ct
                );
            total += updated;
            if (updated < batchSize)
                return total;
        }

        return total;
    }

    public async Task<int> DeleteRevokedAsync(DateTime revokedBeforeUtc, int batchSize, CancellationToken ct)
    {
        var total = 0;
        while (!ct.IsCancellationRequested)
        {
            var deleted = await db
                .PushDeviceTokens.IgnoreQueryFilters()
                .Where(device => !device.IsActive && device.RevokedAtUtc < revokedBeforeUtc)
                .OrderBy(device => device.Id)
                .Take(batchSize)
                .ExecuteDeleteAsync(ct);
            total += deleted;
            if (deleted < batchSize)
                return total;
        }

        return total;
    }
}
