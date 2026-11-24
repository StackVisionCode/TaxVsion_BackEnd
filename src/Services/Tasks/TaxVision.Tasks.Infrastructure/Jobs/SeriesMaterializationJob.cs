using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.Jobs;

/// <summary>
/// Red de seguridad, no el camino normal: la ocurrencia siguiente la siembra el cierre de la anterior.
/// Este barrido existe para las series que quedaron sin instancia abierta porque algo falló a mitad —
/// un reinicio entre el cierre y el guardado, por ejemplo.
///
/// <para>Cruza tenants a propósito: no hay actor autenticado, así que lee con
/// <c>IgnoreQueryFilters()</c>.</para>
/// </summary>
public sealed class SeriesMaterializationJob(IServiceProvider serviceProvider, ILogger<SeriesMaterializationJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceSafeAsync(stoppingToken);
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceSafeAsync(CancellationToken ct)
    {
        try
        {
            await RunOnceAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Series materialization sweep failed; retrying next interval.");
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var pending = await ClaimPendingAsync(ct);

        if (pending.Count == 0)
            return;

        var materialized = 0;
        var summary = await TransactionalJobUnit.RunEachAsync<(Guid TenantId, Guid SeriesId), TasksDbContext>(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            pending,
            async (item, unit, token) =>
            {
                var created = await MaterializeSeriesAsync(unit, item.TenantId, item.SeriesId, token);
                materialized += created is true ? 1 : 0;
                return created is not null;
            },
            logger,
            ct
        );

        logger.LogInformation(
            "Series sweep materialized {Materialized} of {Pending} series without an open instance ({Conflicts} conflicts, {Failed} failed).",
            materialized,
            pending.Count,
            summary.Conflicts,
            summary.Failed
        );
    }

    /// <summary>
    /// Reclama cada pendiente en su propia transacción: cuenta el intento y fija cuándo se reintenta si falla. Un choque
    /// en una serie (otra réplica la reclamó) solo la deja fuera a ella.
    /// </summary>
    private async Task<IReadOnlyList<(Guid TenantId, Guid SeriesId)>> ClaimPendingAsync(CancellationToken ct)
    {
        IReadOnlyList<(Guid TenantId, Guid SeriesId)> pending;
        var now = DateTime.UtcNow;
        using (var scope = serviceProvider.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ITaskSeriesRepository>();
            pending =
            [
                .. (await repository.ListPendingMaterializationAsync(BatchSize, now, ct)).Select(s =>
                    (s.TenantId, s.Id)
                ),
            ];
        }

        var claimed = new List<(Guid TenantId, Guid SeriesId)>();
        foreach (var item in pending)
            if (await ClaimAsync(item.TenantId, item.SeriesId, now, ct))
                claimed.Add(item);
        return claimed;
    }

    private async Task<bool> ClaimAsync(Guid tenantId, Guid seriesId, DateTime now, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var found = await scope
            .ServiceProvider.GetRequiredService<ITaskSeriesRepository>()
            .GetByIdAsync(tenantId, seriesId, ct);
        if (found.IsFailure)
            return false;
        found.Value.ClaimForMaterialization(now);
        try
        {
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException or ConflictException)
        {
            logger.LogInformation("Series {SeriesId} skipped: another instance claimed it.", seriesId);
            return false;
        }
    }

    /// <summary>
    /// Una unidad por serie (TransactionalJobUnit): la instancia y su evento de alta se guardan juntos, y el
    /// handler del evento la anuncia. Devuelve null si no había nada que hacer; false si la regla se agotó
    /// (se guarda el fin de la serie). Un conflicto en una serie no arrastra a las demás.
    /// </summary>
    private static async Task<bool?> MaterializeSeriesAsync(
        JobUnitContext<TasksDbContext> unit,
        Guid tenantId,
        Guid seriesId,
        CancellationToken ct
    )
    {
        var repository = unit.Services.GetRequiredService<ITaskSeriesRepository>();
        var materializer = unit.Services.GetRequiredService<ITaskSeriesMaterializer>();

        var found = await repository.GetByIdAsync(tenantId, seriesId, ct);
        if (found.IsFailure || found.Value is not { Status: SeriesStatus.Active, OpenInstanceId: null })
            return null;

        var created = await materializer.MaterializeNextAsync(found.Value, null, null, ct);
        return created.IsSuccess;
    }
}
