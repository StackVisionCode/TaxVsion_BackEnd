using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Settings;
using Wolverine;

namespace TaxVision.Tasks.Infrastructure.ClientForms;

// Una vez por arranque: publica el flag de push de cada tenant con fecha mínima (ver el backfill).
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
            using var scope = scopeFactory.CreateScope();
            var settings = await scope
                .ServiceProvider.GetRequiredService<IFormModuleSettingsRepository>()
                .ListAsync(stoppingToken);
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            var commands = ClientFormNotificationSettingsBackfill.Commands(settings, Guid.NewGuid().ToString("N"));
            foreach (var command in commands)
                await bus.PublishAsync(command);
            logger.LogInformation("Client form push settings backfill sent {Count} tenant(s).", commands.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Client form push settings backfill failed; it runs again on the next start.");
        }
    }
}
