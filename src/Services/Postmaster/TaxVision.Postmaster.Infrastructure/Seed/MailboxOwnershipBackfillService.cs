using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Projections.ConnectorsEvents;

namespace TaxVision.Postmaster.Infrastructure.Seed;

/// <summary>
/// DM-02: al arrancar, reconcilia con Connectors el dueño de los buzones proyectados antes de este campo. Mientras
/// una fila no tenga dueño conocido no se usa para correos de la oficina (salen por el sistema). No bloquea el
/// arranque; si Connectors no responde, reintenta un par de veces y si no, el próximo arranque.
/// </summary>
public sealed class MailboxOwnershipBackfillService(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<MailboxOwnershipBackfillService> logger
) : DeferredStartupHostedService(lifetime, logger)
{
    // Igual que el directorio de oficinas: en frío Auth puede no estar escuchando todavía.
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var outcome = await RunOnceAsync(cancellationToken);
        foreach (var delay in RetryDelays)
        {
            if (outcome.ConnectorsReachable || cancellationToken.IsCancellationRequested)
                break;
            await Task.Delay(delay, cancellationToken);
            outcome = await RunOnceAsync(cancellationToken);
        }

        if (outcome.Pending == 0)
            logger.LogInformation("Connected mailbox owners already known; nothing to backfill.");
        else
            logger.LogInformation(
                "Mailbox owner backfill: {Applied} of {Pending} row(s) reconciled (Connectors reachable: {Reachable}).",
                outcome.Applied,
                outcome.Pending,
                outcome.ConnectorsReachable
            );
    }

    private async Task<MailboxOwnershipBackfillOutcome> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await MailboxOwnershipBackfill.RunAsync(
            scope.ServiceProvider.GetRequiredService<IConnectorsMailboxDirectory>(),
            scope.ServiceProvider.GetRequiredService<IConnectedMailboxRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            ct
        );
    }
}
