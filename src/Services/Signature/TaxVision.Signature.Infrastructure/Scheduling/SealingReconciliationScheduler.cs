using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.Signature.Infrastructure.Scheduling;

/// <summary>
/// SIG-2: cada <see cref="SealingReconciliationOptions.Interval"/> vuelve a publicar
/// <c>SignatureDocumentsReadyForSealing</c> para las solicitudes <c>Completed</c> con documentos sin sellar,
/// sin <c>SealingFailedAtUtc</c> y completadas hace más de <see cref="SealingReconciliationOptions.Grace"/>.
/// El sellado es idempotente: salta lo ya sellado. Una solicitud por unidad de outbox; candado para una réplica.
/// </summary>
public sealed class SealingReconciliationScheduler(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    IOptions<SealingReconciliationOptions> options,
    TimeProvider clock,
    ILogger<SealingReconciliationScheduler> logger
) : BackgroundService
{
    private const string LockKey = "signature:sealing-reconciliation";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            return;

        // Sin esperar el arranque, publicar puede ganarle a Wolverine (WolverineHasNotStartedException).
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        using var timer = new PeriodicTimer(settings.Interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sealing reconciliation iteration failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Una pasada; devuelve cuántas solicitudes volvió a mandar a sellar.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var settings = options.Value;
        using var scope = scopeFactory.CreateScope();
        await using var handle = await scope
            .ServiceProvider.GetRequiredService<IDistributedLock>()
            .AcquireAsync(LockKey, settings.Interval, ct);
        if (!handle.IsAcquired)
            return 0;

        var now = clock.GetUtcNow().UtcDateTime;
        var candidates = await ListCandidatesAsync(
            scope.ServiceProvider.GetRequiredService<SignatureDbContext>(),
            now - settings.Grace,
            settings,
            ct
        );

        foreach (var candidate in candidates)
        {
            using var unit = scopeFactory.CreateScope();
            var outbox = unit.ServiceProvider.GetRequiredService<IDbContextOutbox<SignatureDbContext>>();
            await outbox.PublishAsync(BuildEvent(candidate, now));
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
        }

        if (candidates.Count > 0)
            logger.LogWarning("Sealing reconciliation re-requested sealing for {Count} request(s).", candidates.Count);
        return candidates.Count;
    }

    private static async Task<IReadOnlyList<Candidate>> ListCandidatesAsync(
        SignatureDbContext db,
        DateTime completedBeforeUtc,
        SealingReconciliationOptions settings,
        CancellationToken ct
    )
    {
        var query = db
            .SignatureRequests.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(request =>
                request.Status == SignatureRequestStatus.Completed
                && request.SealingFailedAtUtc == null
                && request.CompletedAtUtc < completedBeforeUtc
                && request.Documents.Any(document => document.SealedFileId == null)
            );
        if (settings.CompletedAfterUtc is { } completedAfter)
            query = query.Where(request => request.CompletedAtUtc > completedAfter);

        return await query
            .OrderBy(request => request.CompletedAtUtc)
            .Take(settings.BatchSize)
            .Select(request => new Candidate(
                request.TenantId,
                request.Id,
                request.CreatedByUserId,
                request
                    .Documents.Where(document => document.SealedFileId == null)
                    .Select(document => document.Id)
                    .ToList()
            ))
            .ToListAsync(ct);
    }

    private static SignatureDocumentsReadyForSealingIntegrationEvent BuildEvent(Candidate candidate, DateTime now) =>
        new()
        {
            TenantId = candidate.TenantId,
            CorrelationId = Guid.NewGuid().ToString("N"),
            SignatureRequestId = candidate.SignatureRequestId,
            CreatedByUserId = candidate.CreatedByUserId,
            DocumentIds = candidate.DocumentIds,
            ReadyAtUtc = now,
            IdempotencyKey =
                $"signature.documents_ready:{candidate.SignatureRequestId:N}:{string.Join('-', candidate.DocumentIds.Order().Select(id => id.ToString("N")))}:reconcile:{now:yyyyMMddHHmm}",
        };

    private sealed record Candidate(
        Guid TenantId,
        Guid SignatureRequestId,
        Guid CreatedByUserId,
        IReadOnlyList<Guid> DocumentIds
    );
}
