using System.Linq.Expressions;
using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Mobile.Domain.Trips;
using TaxVision.Mobile.Infrastructure.Persistence;
using TaxVision.Mobile.Infrastructure.Scheduling;

namespace TaxVision.Mobile.Infrastructure.Retention;

/// <summary>
/// Borra el recorrido GPS (los puntos) de los viajes que pasaron el plazo de su oficina; el resumen del viaje
/// se conserva. El lock de Redis deja una sola réplica a la vez. Sin Redis no hay lock y la pasada no purga
/// (igual que <c>ActivityRetentionJob</c>): cuenta como fallida y se reintenta en minutos.
/// </summary>
internal sealed class MileageGpsRetentionJob(
    IServiceScopeFactory scopes,
    IDistributedLockFactory locks,
    IOptions<MileageGpsRetentionOptions> options,
    TimeProvider clock,
    ILogger<MileageGpsRetentionJob> log
) : BackgroundService
{
    private const string LockKey = "job:mileage-gps-retention";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var scope = scopes.CreateScope())
            await scope
                .ServiceProvider.GetRequiredService<IHostApplicationLifetime>()
                .WaitForApplicationStartedAsync(stoppingToken);

        var settings = options.Value;
        if (!settings.Enabled)
        {
            log.LogInformation("Mileage GPS retention is disabled by configuration; not running.");
            return;
        }

        var interval = TimeSpan.FromHours(settings.IntervalHours);
        while (!stoppingToken.IsCancellationRequested)
        {
            var completed = await RunOnceSafeAsync(stoppingToken);
            try
            {
                await Task.Delay(ReconciliationRetry.NextDelay(completed, interval), clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>false si la pasada falló entera (sin lock por Redis caído, o sin base): el job reintenta pronto.</summary>
    internal async Task<bool> RunOnceSafeAsync(CancellationToken ct)
    {
        try
        {
            await RunOnceAsync(ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return true;
        }
        catch (Exception ex)
        {
            log.LogError(
                ex,
                "Mileage GPS retention pass failed; retrying in {RetryMinutes} min.",
                ReconciliationRetry.AfterFailure.TotalMinutes
            );
            return false;
        }
    }

    /// <summary>Una pasada con el lock tomado. null si otra réplica lo tiene: no se purga nada.</summary>
    internal async Task<int?> RunOnceAsync(CancellationToken ct)
    {
        await using var handle = await locks.TryAcquireAsync(
            LockKey,
            TimeSpan.FromMinutes(options.Value.LockTtlMinutes),
            ct
        );
        if (handle is null)
        {
            log.LogDebug("Mileage GPS retention skipped this pass: another replica holds the lock.");
            return null;
        }

        var purged = await PurgeAsync(ct);
        if (purged > 0)
            log.LogInformation("Mileage GPS retention removed the route of {Trips} trip(s).", purged);
        return purged;
    }

    /// <summary>La purga sin lock; devuelve cuántos viajes perdieron su recorrido.</summary>
    internal async Task<int> PurgeAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var periods = await OfficePeriodsAsync(now, ct);

        var purged = 0;
        foreach (var (tenantId, retentionDays) in periods)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                purged += await PurgeOfficeAsync(tenantId, now.AddDays(-retentionDays), now, settings, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Esta oficina falló o alguien cambió un viaje a la vez: se salta y siguen las demás.
                log.LogWarning(ex, "Mileage GPS retention skipped office {TenantId}; next pass retries.", tenantId);
            }
        }

        return purged;
    }

    private async Task<Dictionary<Guid, int>> OfficePeriodsAsync(DateTime now, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MobileDbContext>();

        // Job entre oficinas: solo trae el plazo de cada una; cada purga va después con su oficina explícita.
        var periods = await db
            .MileageRetentionPolicies.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.RetentionDays != null)
            .ToDictionaryAsync(p => p.TenantId, p => p.RetentionDays!.Value, ct);

        // Oficinas sin plazo configurado con viajes que ya pasaron el plazo por defecto. Consulta de job
        // entre oficinas: solo trae ids de oficina; cada purga va después con su oficina explícita.
        var unconfigured = await db
            .Trips.IgnoreQueryFilters()
            .Where(t => t.DeletedAtUtc == null && t.CoordinatesPurgedAtUtc == null)
            .Where(Expired(now.AddDays(-MileageRetentionPolicy.DefaultRetentionDays)))
            .Select(t => t.TenantId)
            .Distinct()
            .ToListAsync(ct);
        foreach (var tenantId in unconfigured)
            periods.TryAdd(tenantId, MileageRetentionPolicy.DefaultRetentionDays);

        return periods;
    }

    // Fecha de referencia por estado. Terminado (Completed o Cancelled): cuándo terminó. Sin terminar (en
    // curso o en pausa): la última vez que el servidor supo de él (UpdatedAtUtc, reloj del servidor); así
    // uno abandonado se purga y uno con actividad reciente no se toca.
    private static Expression<Func<Trip, bool>> Expired(DateTime cutoff) =>
        t =>
            ((t.Status == TripStatus.Completed || t.Status == TripStatus.Cancelled) && t.EndedAtUtc <= cutoff)
            || ((t.Status == TripStatus.InProgress || t.Status == TripStatus.Paused) && t.UpdatedAtUtc <= cutoff);

    private async Task<int> PurgeOfficeAsync(
        Guid tenantId,
        DateTime cutoff,
        DateTime now,
        MileageGpsRetentionOptions settings,
        CancellationToken ct
    )
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MobileDbContext>();

        // Reparación: viajes ya marcados como purgados que conservaron su recorrido.
        // Sin filtro global: el job no tiene oficina en contexto; la oficina va explícita.
        await db
            .TripPoints.IgnoreQueryFilters()
            .Where(p =>
                p.TenantId == tenantId
                && db.Trips.IgnoreQueryFilters()
                    .Any(t => t.TenantId == tenantId && t.Id == p.TripId && t.CoordinatesPurgedAtUtc != null)
            )
            .ExecuteDeleteAsync(ct);

        // Se repiten lotes hasta agotar los vencidos o llegar al tope de la pasada.
        var purged = 0;
        for (var batch = 0; batch < settings.MaxBatchesPerOffice; batch++)
        {
            var count = await PurgeBatchAsync(db, tenantId, cutoff, now, settings.BatchSize, ct);
            purged += count;
            if (count < settings.BatchSize)
                break;
        }

        return purged;
    }

    private static async Task<int> PurgeBatchAsync(
        MobileDbContext db,
        Guid tenantId,
        DateTime cutoff,
        DateTime now,
        int batchSize,
        CancellationToken ct
    )
    {
        // Sin filtro global: la oficina va explícita en cada consulta del lote.
        var candidates = await db
            .Trips.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.DeletedAtUtc == null && t.CoordinatesPurgedAtUtc == null)
            .Where(Expired(cutoff))
            .OrderBy(t => t.EndedAtUtc ?? t.UpdatedAtUtc)
            .ThenBy(t => t.Id)
            .Take(batchSize)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return 0;

        // El recorrido son los puntos: se borran antes de marcar. Si el marcado falla, el viaje sigue
        // siendo candidato y la próxima pasada lo completa. Solo se borra si el viaje sigue vencido:
        // uno que acaba de reportar no pierde lo nuevo.
        var tripIds = candidates.Select(t => t.Id).ToList();
        var stillExpired = db.Trips.IgnoreQueryFilters().Where(t => t.TenantId == tenantId).Where(Expired(cutoff));
        await db
            .TripPoints.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && tripIds.Contains(p.TripId) && stillExpired.Any(t => t.Id == p.TripId))
            .ExecuteDeleteAsync(ct);
        foreach (var trip in candidates)
            trip.PurgeCoordinates(now);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return candidates.Count;
    }
}
