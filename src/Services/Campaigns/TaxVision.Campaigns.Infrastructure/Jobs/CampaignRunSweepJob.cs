using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Runs.Commands;
using Wolverine;

namespace TaxVision.Campaigns.Infrastructure.Jobs;

/// <summary>Opciones del barrido de runs que no cierran (<c>Campaigns:RunSweep</c>).</summary>
public sealed class CampaignRunSweepOptions
{
    public const string SectionName = "Campaigns:RunSweep";

    /// <summary>Apagado hasta reprocesar la DLQ de resultados (si no, marcaría Unknown lo que está en la DLQ).</summary>
    public bool Enabled { get; set; }

    /// <summary>Edad a partir de la cual un run en Dispatching se cierra por tiempo (por encima del DLR típico de SMS).</summary>
    public double StaleAfterHours { get; set; } = 48;

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);

    public int BatchSize { get; set; } = 100;
}

/// <summary>
/// Barrido de runs que no cierran (CMP-03): por cada run viejo en <c>Dispatching</c> invoca
/// <see cref="SweepStaleCampaignRunCommand"/>, que corre con la transacción y el outbox de su handler. El job
/// no guarda ni publica por su cuenta. Sin candado de líder: la RowVersion del run hace no-op al segundo.
/// </summary>
public sealed class CampaignRunSweepJob(
    IServiceProvider services,
    IOptionsMonitor<CampaignRunSweepOptions> options,
    ILogger<CampaignRunSweepJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var current = options.CurrentValue;
            if (current.Enabled)
            {
                try
                {
                    await SweepAsync(current, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Campaign run sweep failed; will retry next interval.");
                }
            }

            try
            {
                await Task.Delay(current.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SweepAsync(CampaignRunSweepOptions current, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<ICampaignRunRepository>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var createdBefore = DateTime.UtcNow.AddHours(-current.StaleAfterHours);
        var stale = await runs.ListStaleDispatchingAsync(createdBefore, current.BatchSize, ct);
        foreach (var run in stale)
        {
            // Un run que falla no corta el lote.
            try
            {
                await bus.InvokeAsync(new SweepStaleCampaignRunCommand(run.TenantId, run.Id), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sweep of campaign run {RunId} failed; next interval retries.", run.Id);
            }
        }

        if (stale.Count > 0)
            logger.LogInformation("Campaign run sweep processed {Count} stale runs.", stale.Count);
    }
}
