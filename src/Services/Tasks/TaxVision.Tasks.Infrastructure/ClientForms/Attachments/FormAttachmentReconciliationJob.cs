using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Attachments;

internal sealed class FormAttachmentReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IOptions<ClientFormsOptions> options,
    ILogger<FormAttachmentReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await scopeFactory
            .CreateScope()
            .ServiceProvider.GetRequiredService<IHostApplicationLifetime>()
            .WaitForApplicationStartedAsync(stoppingToken);

        var settings = options.Value.Attachments;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(settings.ReconciliationIntervalMinutes));
        do
        {
            try
            {
                await ReconcileOnceAsync(settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Client form attachment reconciliation failed; retrying on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ReconcileOnceAsync(ClientFormsAttachmentOptions settings, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var reconciler = scope.ServiceProvider.GetRequiredService<FormAttachmentReconciler>();
        var count = await reconciler.ReconcileAsync(
            TimeSpan.FromMinutes(settings.ReconcileAfterMinutes),
            TimeSpan.FromMinutes(settings.PendingExpiryMinutes),
            200,
            ct
        );
        if (count > 0)
            logger.LogInformation("Reconciled {Count} client form attachment(s).", count);
    }
}
