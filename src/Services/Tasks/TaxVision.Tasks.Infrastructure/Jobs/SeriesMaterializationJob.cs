using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

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
        IReadOnlyList<(Guid TenantId, Guid SeriesId)> pending;
        using (var scope = serviceProvider.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ITaskSeriesRepository>();
            pending = (await repository.ListPendingMaterializationAsync(BatchSize, ct))
                .Select(s => (s.TenantId, s.Id))
                .ToList();
        }

        if (pending.Count == 0)
            return;

        var materialized = 0;
        foreach (var (tenantId, seriesId) in pending)
        {
            if (await MaterializeSeriesAsync(tenantId, seriesId, ct))
                materialized++;
        }

        logger.LogInformation(
            "Series sweep materialized {Materialized} of {Pending} series without an open instance.",
            materialized,
            pending.Count
        );
    }

    /// <summary>
    /// Una unidad por serie: materializar, anunciar por el outbox y guardar juntos. Un conflicto en
    /// una serie no arrastra a las demás; el barrido siguiente la reintenta.
    /// </summary>
    private async Task<bool> MaterializeSeriesAsync(Guid tenantId, Guid seriesId, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;
        var repository = services.GetRequiredService<ITaskSeriesRepository>();
        var materializer = services.GetRequiredService<ITaskSeriesMaterializer>();
        var outbox = services.GetRequiredService<IDbContextOutbox<TasksDbContext>>();
        var correlation = services.GetRequiredService<ICorrelationContext>();

        var found = await repository.GetByIdAsync(tenantId, seriesId, ct);
        if (found.IsFailure || found.Value is not { Status: SeriesStatus.Active, OpenInstanceId: null })
            return false;

        var created = await materializer.MaterializeNextAsync(found.Value, null, null, ct);
        if (created.IsSuccess)
            await TaskBirthAnnouncements.PublishAsync(created.Value, outbox, correlation, announceAssignment: false);

        try
        {
            // También guarda el fin de la serie cuando la regla se agotó.
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
        }
        catch (ConflictException ex) when (ex.Code == "Persistence.ConcurrencyConflict")
        {
            logger.LogDebug("Series sweep skipped series {SeriesId} modified concurrently.", seriesId);
            return false;
        }

        return created.IsSuccess;
    }
}
