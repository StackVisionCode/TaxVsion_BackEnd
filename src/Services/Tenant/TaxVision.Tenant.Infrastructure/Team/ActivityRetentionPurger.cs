using Microsoft.EntityFrameworkCore;
using TaxVision.Tenant.Infrastructure.Persistence;

namespace TaxVision.Tenant.Infrastructure.Team;

/// <summary>
/// Borra un lote de entradas vencidas. Mantenimiento por fecha de todas las oficinas: el job no tiene tenant y no
/// devuelve filas a nadie, por eso IgnoreQueryFilters() sin tenant.
/// </summary>
public sealed class ActivityRetentionPurger(TenantDbContext db)
{
    public async Task<int> PurgeBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default)
    {
        var ids = await db
            .ActivityEntries.IgnoreQueryFilters()
            .Where(e => e.OccurredAtUtc < cutoffUtc)
            .OrderBy(e => e.OccurredAtUtc)
            .Select(e => e.Id)
            .Take(batchSize)
            .ToListAsync(ct);
        if (ids.Count == 0)
            return 0;

        return await db.ActivityEntries.IgnoreQueryFilters().Where(e => ids.Contains(e.Id)).ExecuteDeleteAsync(ct);
    }
}
