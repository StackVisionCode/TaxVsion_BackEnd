using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.Infrastructure.Observability;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Observability;

// Refresca el censo de ciphertext por versión (X-9.3) para el gauge y el health check; si una lectura
// falla, el censo anterior sigue vigente. Es una agregación por tabla, no descifra.
internal sealed class ClientFormsKeyVersionJob(
    IServiceScopeFactory scopeFactory,
    ClientFormsMetrics metrics,
    ILogger<ClientFormsKeyVersionJob> logger
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
                    "Client forms key version census could not be refreshed; retrying on the next tick."
                );
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RefreshOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IClientFormsCiphertextStore>();
        metrics.UpdateCiphertextVersions(await store.CountByKeyVersionAsync(ct));
    }
}
