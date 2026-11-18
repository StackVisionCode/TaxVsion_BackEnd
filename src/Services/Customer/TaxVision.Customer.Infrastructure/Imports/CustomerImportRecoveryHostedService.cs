using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Imports.Configuration;
using TaxVision.Customer.Application.Imports.Messages;

namespace TaxVision.Customer.Infrastructure.Imports;

/// <summary>Cada pocos minutos vuelve a encolar (o da por fallidos) los imports sin avance.</summary>
internal sealed class CustomerImportRecoveryHostedService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<CustomerImportRecoveryOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<CustomerImportRecoveryHostedService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var current = options.CurrentValue;
            if (current.Enabled)
            {
                try
                {
                    var recovered = await CustomerImportRecovery.RecoverAsync(
                        scopeFactory,
                        DateTime.UtcNow,
                        current,
                        logger,
                        stoppingToken
                    );
                    if (recovered > 0)
                        logger.LogInformation("Customer import recovery handled {Count} stuck imports", recovered);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Customer import recovery iteration failed");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, current.IntervalMinutes)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
