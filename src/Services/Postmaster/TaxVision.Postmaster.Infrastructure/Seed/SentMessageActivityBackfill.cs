using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.ValueObjects;
using TaxVision.Postmaster.Infrastructure.Persistence;

namespace TaxVision.Postmaster.Infrastructure.Seed;

/// <summary>
/// Reposición de PMS-03: recalcula <c>Activity</c> de los correos que Q-PMS-2 devuelve, con la misma función del
/// dominio. No toca el <c>Outcome</c> de los destinatarios ni publica avisos. Idempotente: lo corregido ya no sale
/// en la consulta.
/// </summary>
public static class SentMessageActivityBackfill
{
    /// <param name="Found">Correos que oculta un rebote o una queja (Q-PMS-2).</param>
    /// <param name="Changed">Los que cambiaron, o cambiarían en una corrida de prueba.</param>
    /// <param name="Skipped">Los que un aviso cambió mientras tanto: se ven en la próxima corrida.</param>
    public readonly record struct BackfillOutcome(int Found, int Changed, int Skipped);

    public static async Task<BackfillOutcome> RunAsync(
        PostmasterDbContext db,
        int batchSize,
        bool dryRun,
        CancellationToken ct
    )
    {
        int found = 0,
            changed = 0,
            skipped = 0;
        Guid? afterId = null;
        while (true)
        {
            var ids = await PageAsync(db, afterId, batchSize, ct);
            if (ids.Count == 0)
                break;

            afterId = ids[^1];
            found += ids.Count;
            var messages = await db
                .SentMessages.IgnoreQueryFilters()
                .Include(m => m.Recipients)
                .Where(m => ids.Contains(m.Id))
                .ToListAsync(ct);
            var inBatch = messages.Count(message => message.RecalculateActivity());

            if (dryRun)
                changed += inBatch;
            else
            {
                try
                {
                    await db.SaveChangesAsync(ct);
                    changed += inBatch;
                }
                catch (ConflictException)
                {
                    skipped += inBatch;
                }
            }
            db.ChangeTracker.Clear();
        }

        return new BackfillOutcome(found, changed, skipped);
    }

    private static Task<List<Guid>> PageAsync(
        PostmasterDbContext db,
        Guid? afterId,
        int batchSize,
        CancellationToken ct
    )
    {
        var query = db
            .SentMessages.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m =>
                m.Status == SentMessageStatus.Sent
                && (
                    m.Activity == SentMessageActivity.Sent
                    || m.Activity == SentMessageActivity.Delivered
                    || m.Activity == SentMessageActivity.Deferred
                )
                && m.Recipients.Any(r =>
                    r.Outcome == DeliveryOutcome.HardBounced
                    || r.Outcome == DeliveryOutcome.SoftBounced
                    || r.Outcome == DeliveryOutcome.Rejected
                    || r.Outcome == DeliveryOutcome.Complained
                )
            );
        if (afterId is { } after)
            query = query.Where(m => m.Id.CompareTo(after) > 0);

        return query.OrderBy(m => m.Id).Select(m => m.Id).Take(Math.Max(1, batchSize)).ToListAsync(ct);
    }
}
