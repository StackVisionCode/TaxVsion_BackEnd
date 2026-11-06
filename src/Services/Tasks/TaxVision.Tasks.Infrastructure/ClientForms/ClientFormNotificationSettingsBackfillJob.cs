using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Settings;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms;

// Una vez por arranque: publica el flag de push de cada tenant con fecha mínima (ver el backfill).
// Los avisos salen por el outbox en una unidad transaccional: o salen todos o ninguno.
internal sealed class ClientFormNotificationSettingsBackfillJob(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<ClientFormNotificationSettingsBackfillJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await lifetime.WaitForApplicationStartedAsync(stoppingToken);
            var sent = 0;
            var result = await TransactionalJobUnit.RunAsync<TasksDbContext>(
                scopeFactory,
                async (unit, ct) =>
                {
                    var settings = await unit.Services.GetRequiredService<IFormModuleSettingsRepository>().ListAsync(ct);
                    var commands = ClientFormNotificationSettingsBackfill.Commands(settings, Guid.NewGuid().ToString("N"));
                    foreach (var command in commands)
                        await unit.PublishAsync(command);
                    sent = commands.Count;
                    return sent > 0;
                },
                logger,
                stoppingToken
            );
            logger.LogInformation(
                "Client form push settings backfill {Outcome}: {Count} tenant(s).",
                result.Outcome,
                sent
            );
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
