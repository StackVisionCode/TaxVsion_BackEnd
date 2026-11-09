using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notes.Application.Notes.Commands;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Runtime;

namespace TaxVision.Notes.Infrastructure.Jobs;

public sealed class PendingAttachmentReconciliationOptions
{
    public const string SectionName = "Notes:PendingAttachmentReconciliation";

    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 15;

    /// <summary>Por debajo de esto el escaneo puede seguir en curso y preguntar sería ruido.</summary>
    public int GraceMinutes { get; set; } = 10;

    public int BatchSize { get; set; } = 100;
}

/// <summary>
/// NTS-1: busca adjuntos Pending/UnderReview viejos (cross-tenant) e invoca un
/// <see cref="ReconcilePendingAttachmentCommand"/> por adjunto, cada uno con su transacción en el
/// pipeline de Wolverine. Un fallo no frena a los demás; se reintenta en la próxima pasada.
/// </summary>
public sealed class PendingAttachmentReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IWolverineRuntime runtime,
    IHostApplicationLifetime lifetime,
    IOptions<PendingAttachmentReconciliationOptions> options,
    ILogger<PendingAttachmentReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            return;

        // Sin esperar el arranque, invocar puede ganarle a Wolverine (WolverineHasNotStartedException).
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, settings.IntervalMinutes)));
        do
        {
            try
            {
                await RunOnceAsync(settings, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Pending attachment reconciliation pass failed; retrying on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(PendingAttachmentReconciliationOptions settings, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-Math.Max(0, settings.GraceMinutes));
        var batchSize = Math.Max(1, settings.BatchSize);

        // Bus propio (no del scope): cada comando corre con su transacción.
        var bus = new MessageBus(runtime);
        DateTime? after = null;
        var processed = 0;
        var failed = 0;

        while (true)
        {
            var page = await LoadPageAsync(cutoff, after, batchSize, ct);
            foreach (var item in page)
            {
                try
                {
                    await bus.InvokeAsync(
                        new ReconcilePendingAttachmentCommand(item.TenantId, item.NoteId, item.FileId),
                        ct
                    );
                    processed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    logger.LogWarning(
                        ex,
                        "Reconcile failed for attachment file {FileId} on note {NoteId}.",
                        item.FileId,
                        item.NoteId
                    );
                }
            }

            if (page.Count < batchSize)
                break;
            after = page[^1].LinkedAtUtc;
        }

        if (processed + failed > 0)
            logger.LogInformation(
                "Pending attachment reconciliation checked {Processed} attachment(s), {Failed} failed.",
                processed,
                failed
            );
    }

    private async Task<List<PendingAttachment>> LoadPageAsync(
        DateTime cutoff,
        DateTime? after,
        int batchSize,
        CancellationToken ct
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotesDbContext>();

        // Cursor por LinkedAtUtc: lo ya visto en esta pasada no se repite.
        return await PendingQuery(db, cutoff, after ?? DateTime.MinValue).Take(batchSize).ToListAsync(ct);
    }

    /// <summary>Adjuntos Pending/UnderReview vinculados entre <paramref name="cursor"/> y <paramref name="cutoff"/>, de todos los tenants.</summary>
    public static IQueryable<PendingAttachment> PendingQuery(NotesDbContext db, DateTime cutoff, DateTime cursor) =>
        from a in db.NoteAttachments.AsNoTracking()
        join n in db.Notes.IgnoreQueryFilters().AsNoTracking() on a.NoteId equals n.Id
        where
            (a.Status == NoteAttachmentStatus.Pending || a.Status == NoteAttachmentStatus.UnderReview)
            && a.LinkedAtUtc < cutoff
            && a.LinkedAtUtc > cursor
        orderby a.LinkedAtUtc
        select new PendingAttachment(n.TenantId, a.NoteId, a.CloudStorageFileId, a.LinkedAtUtc);

    public sealed record PendingAttachment(Guid TenantId, Guid NoteId, Guid FileId, DateTime LinkedAtUtc);
}
