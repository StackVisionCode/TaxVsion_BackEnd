using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.MailboxListener.Options;

namespace TaxVision.Connectors.MailboxListener.Coordination;

/// <summary>Hace girar al coordinador cada <see cref="MailboxListenerOptions.TickSeconds"/>.</summary>
public sealed class MailboxListenerService(
    MailboxListenerCoordinator coordinator,
    IHostApplicationLifetime lifetime,
    IOptions<MailboxListenerOptions> options,
    TimeProvider time,
    ILogger<MailboxListenerService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Mailbox listener is disabled; IMAP stays on the reconciliation job.");
            return;
        }

        // Publica por Wolverine: antes de que arranque revienta con WolverineHasNotStartedException.
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        var tick = TimeSpan.FromSeconds(Math.Max(5, options.Value.TickSeconds));
        using var timer = new PeriodicTimer(tick, time);
        try
        {
            do
            {
                await TickSafeAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado normal.
        }
        finally
        {
            await coordinator.StopAllAsync();
        }
    }

    private async Task TickSafeAsync(CancellationToken ct)
    {
        try
        {
            await coordinator.TickAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Base o Redis caídos: las sesiones abiertas siguen; se reintenta en la próxima vuelta.
            logger.LogError(ex, "Mailbox listener tick failed");
        }
    }
}
