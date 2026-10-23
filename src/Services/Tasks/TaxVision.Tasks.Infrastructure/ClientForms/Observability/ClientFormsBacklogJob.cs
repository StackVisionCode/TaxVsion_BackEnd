using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Infrastructure.Observability;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Observability;

// Refresca la foto de los gauges; si una lectura falla, los gauges siguen con la anterior.
internal sealed class ClientFormsBacklogJob(
    IServiceScopeFactory scopeFactory,
    ClientFormsMetrics metrics,
    ILogger<ClientFormsBacklogJob> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await scopeFactory
            .CreateScope()
            .ServiceProvider.GetRequiredService<IHostApplicationLifetime>()
            .WaitForApplicationStartedAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RefreshOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Client forms backlog metrics could not be refreshed; retrying on the next tick."
                );
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RefreshOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<ClientFormsBacklogReader>();
        metrics.UpdateBacklog(await reader.ReadAsync(DateTime.UtcNow, ct));
    }
}
