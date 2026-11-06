using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Tenant.Application.Compliance.Abstractions;
using TaxVision.Tenant.Application.Compliance.Reminders;
using TaxVision.Tenant.Infrastructure.Persistence;
using TaxVision.Tenant.Infrastructure.Scheduling;

namespace TaxVision.Tenant.Infrastructure.Compliance;

/// <summary>
/// Recordatorio anual del WISP. Recorre por lotes las oficinas activas que pueden estar en ventana y, una por
/// una, deja que el agregado decida y anote el aviso; la fila y el aviso se confirman juntos en una unidad.
///
/// <para>
/// Varias réplicas: el lock de Redis deja una sola pasada a la vez, y si dos llegaran a la misma oficina el
/// token de concurrencia de <c>WispReminderSentOn</c> hace que la segunda choque y la salte sin publicar.
/// Correr dos veces el mismo día no repite: el agregado ya lo tiene anotado.
/// </para>
/// </summary>
internal sealed class WispReviewReminderJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory locks,
    IOptions<WispReviewReminderOptions> options,
    ILogger<WispReviewReminderJob> logger
) : BackgroundService
{
    private const string LockKey = "job:wisp-review-reminders";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wolverine aún no arrancó cuando arrancan los hosted services: sin la espera la primera pasada se pierde.
        using (var scope = scopeFactory.CreateScope())
            await scope
                .ServiceProvider.GetRequiredService<IHostApplicationLifetime>()
                .WaitForApplicationStartedAsync(stoppingToken);

        var settings = options.Value;
        if (!settings.Enabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(settings.IntervalMinutes));
        do
        {
            try
            {
                await RunGuardedAsync(settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "WispReviewReminderJob failed; retrying on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunGuardedAsync(WispReviewReminderOptions settings, CancellationToken ct)
    {
        await using var handle = await locks.TryAcquireAsync(
            LockKey,
            TimeSpan.FromMinutes(settings.LockTtlMinutes),
            ct
        );
        if (handle is null)
        {
            logger.LogDebug("WispReviewReminderJob skipped this tick: another replica holds the lock.");
            return;
        }

        var sent = await SweepAsync(settings.BatchSize, DateTime.UtcNow, ct);
        if (sent > 0)
            logger.LogInformation("WispReviewReminderJob sent {Sent} WISP review reminder(s).", sent);
    }

    /// <summary>Una pasada completa; devuelve cuántos avisos salieron.</summary>
    internal async Task<int> SweepAsync(int batchSize, DateTime nowUtc, CancellationToken ct)
    {
        // El job es el origen de la traza: un id por pasada.
        var correlationId = Guid.NewGuid().ToString("N");
        var baseOnOrBefore = WispReviewReminders.LatestCandidateBase(nowUtc);
        Guid? after = null;
        var sent = 0;

        while (!ct.IsCancellationRequested)
        {
            IReadOnlyList<WispReminderCandidate> page;
            using (var scope = scopeFactory.CreateScope())
                page = await scope
                    .ServiceProvider.GetRequiredService<IOfficeComplianceRepository>()
                    .ListWispReminderCandidatesAsync(baseOnOrBefore, after, batchSize, ct);

            foreach (var candidate in page)
            {
                if (await TrySendAsync(candidate, nowUtc, correlationId, ct))
                    sent++;
            }

            if (page.Count < batchSize)
                break;
            after = page[^1].TenantId;
        }

        return sent;
    }

    // Una unidad por oficina: una colisión o un fallo en una no arrastra a las demás.
    private async Task<bool> TrySendAsync(
        WispReminderCandidate candidate,
        DateTime nowUtc,
        string correlationId,
        CancellationToken ct
    )
    {
        var result = await TransactionalJobUnit.RunAsync<TenantDbContext>(
            scopeFactory,
            async (unit, token) =>
            {
                using var correlationScope = unit.Services.GetRequiredService<ICorrelationContext>().Push(correlationId);
                var reminder = await WispReviewReminders.RegisterIfDueAsync(
                    candidate,
                    nowUtc,
                    correlationId,
                    unit.Services.GetRequiredService<IOfficeComplianceRepository>(),
                    token
                );
                if (reminder is null)
                    return false;

                await unit.PublishAsync(reminder);
                return true;
            },
            logger,
            ct
        );

        // Un conflicto es normal (otra réplica o un guardado del admin ganó): la próxima pasada lo reevalúa.
        if (result.Outcome == JobUnitOutcome.Failed)
            logger.LogError(result.Exception, "WispReviewReminderJob failed for tenant {TenantId}.", candidate.TenantId);

        return result.Outcome == JobUnitOutcome.Committed;
    }
}
