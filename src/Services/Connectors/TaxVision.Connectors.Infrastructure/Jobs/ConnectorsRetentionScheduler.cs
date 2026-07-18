using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Audit;

namespace TaxVision.Connectors.Infrastructure.Jobs;

/// <summary>
/// Cada 24h purga entradas de ProviderConnectionAuditLog más viejas que <c>RetentionDays</c> (Fase 11,
/// §26/§30 del plan). Deshabilitado por default (<see cref="ConnectorsRetentionOptions.Enabled"/>) —
/// requiere autorización explícita antes de purgar en producción. Purga directa vía
/// <c>ExecuteDeleteAsync</c> (sin change tracking, sin SaveChanges) — el log es append-only, no hay
/// invariante de aggregate que proteger al borrar filas viejas.
/// </summary>
public sealed class ConnectorsRetentionScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<ConnectorsRetentionOptions> options,
    ILogger<ConnectorsRetentionScheduler> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(10);
    private const int MaxBatchesPerRun = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, stoppingToken);
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
            logger.LogError(ex, "ConnectorsRetentionScheduler iteration failed.");
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var opt = options.Value;
        if (!opt.Enabled)
            return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProviderConnectionAuditLogRepository>();

        var cutoff = DateTime.UtcNow.AddDays(-opt.RetentionDays);
        var totalPurged = 0;

        // Batches acotados por corrida — evita que un backlog grande retenga el scope/conexión
        // abiertos indefinidamente; el resto se purga en la siguiente corrida (24h después).
        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
        {
            var deleted = await repository.DeleteOlderThanAsync(cutoff, opt.BatchSize, ct);
            totalPurged += deleted;
            if (deleted < opt.BatchSize)
                break;
        }

        if (totalPurged > 0)
            logger.LogInformation(
                "ConnectorsRetentionScheduler purged {Count} ProviderConnectionAuditLog entries older than {Cutoff}.",
                totalPurged,
                cutoff
            );
    }
}
