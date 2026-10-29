using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Retention;

// X-5 — dispara el barrido de retención por intervalo. Si una corrida falla, reintenta en el siguiente tick.
internal sealed class FormRetentionJob(
    IServiceScopeFactory scopeFactory,
    IOptions<ClientFormsOptions> options,
    ILogger<FormRetentionJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await scopeFactory
            .CreateScope()
            .ServiceProvider.GetRequiredService<IHostApplicationLifetime>()
            .WaitForApplicationStartedAsync(stoppingToken);

        var interval = TimeSpan.FromHours(Math.Max(1, options.Value.Retention.JobIntervalHours));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Client forms retention sweep failed; retrying on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<FormRetentionService>();
        await service.RunAsync(DateTime.UtcNow, ct);
    }
}
