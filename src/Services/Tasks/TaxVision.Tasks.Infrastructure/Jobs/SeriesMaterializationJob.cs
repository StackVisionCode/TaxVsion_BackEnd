using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Series.Abstractions;

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
        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITaskSeriesRepository>();
        var materializer = scope.ServiceProvider.GetRequiredService<ITaskSeriesMaterializer>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var pending = await repository.ListPendingMaterializationAsync(BatchSize, ct);
        if (pending.Count == 0)
            return;

        var materialized = 0;
        foreach (var series in pending)
        {
            var created = await materializer.MaterializeNextAsync(series, null, null, ct);
            if (created.IsSuccess)
                materialized++;
        }

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "Series sweep materialized {Materialized} of {Pending} series without an open instance.",
            materialized,
            pending.Count
        );
    }
}
