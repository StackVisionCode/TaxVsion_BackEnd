using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Notes.Infrastructure.Persistence;

namespace TaxVision.Notes.Infrastructure.Jobs;

/// <summary>
/// N2: al arrancar, rellena <c>ContentPlainText</c> de las notas que aún no lo tienen (creadas antes de la
/// columna). La extracción vive en el dominio (<c>NoteContent.WithPlainText</c>), por eso no es SQL. Una nota
/// por transacción, no toca otros campos y es idempotente: lo ya lleno no vuelve a entrar en el lote.
/// </summary>
public sealed class NotePlainTextBackfillJob(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<NotePlainTextBackfillJob> logger
) : DeferredStartupHostedService(lifetime, logger)
{
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        var filled = await FillAsync(db, BatchSize, logger, cancellationToken);
        if (filled > 0)
            logger.LogInformation("Note plain text backfill filled {Count} note(s).", filled);
    }

    /// <summary>Recorre por lotes las notas sin texto plano. Devuelve cuántas llenó.</summary>
    public static async Task<int> FillAsync(NotesDbContext db, int batchSize, ILogger logger, CancellationToken ct)
    {
        var failed = new List<Guid>();
        var filled = 0;
        while (!ct.IsCancellationRequested)
        {
            var ids = await db
                .Notes.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(n => n.Content.PlainText == null && !failed.Contains(n.Id))
                .OrderBy(n => n.Id)
                .Select(n => n.Id)
                .Take(batchSize)
                .ToListAsync(ct);
            if (ids.Count == 0)
                break;

            foreach (var id in ids)
            {
                if (await FillOneAsync(db, id, logger, ct))
                    filled++;
                else
                    failed.Add(id);
            }
        }
        return filled;
    }

    private static async Task<bool> FillOneAsync(NotesDbContext db, Guid noteId, ILogger logger, CancellationToken ct)
    {
        try
        {
            var note = await db.Notes.IgnoreQueryFilters().FirstOrDefaultAsync(n => n.Id == noteId, ct);
            if (note is null)
                return false;
            note.FillPlainText();
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una nota que falla (p. ej. editada a la vez) no frena al resto; al guardarla se llena sola.
            logger.LogWarning(ex, "Note plain text backfill skipped note {NoteId}.", noteId);
            return false;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
