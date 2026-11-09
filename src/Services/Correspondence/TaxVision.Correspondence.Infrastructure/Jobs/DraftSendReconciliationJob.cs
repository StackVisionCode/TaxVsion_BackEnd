using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Compose;

namespace TaxVision.Correspondence.Infrastructure.Jobs;

/// <summary>Toma los borradores en <c>Sending</c> de más de <c>StuckAfter</c> y los cierra, uno por scope (COR-08).</summary>
public sealed class DraftSendReconciliationJob(
    IServiceProvider serviceProvider,
    IOptions<DraftSendReconciliationOptions> options,
    ILogger<DraftSendReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            return;

        var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "DraftSendReconciliationJob iteration failed.");
            }

            try
            {
                await Task.Delay(settings.Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var settings = options.Value;
        IReadOnlyList<StuckDraftSend> stuck;
        using (var readScope = serviceProvider.CreateScope())
        {
            var drafts = readScope.ServiceProvider.GetRequiredService<IDraftRepository>();
            stuck = await drafts.ListStuckSendingAsync(DateTime.UtcNow - settings.StuckAfter, settings.BatchSize, ct);
        }

        foreach (var draft in stuck)
        {
            using var scope = serviceProvider.CreateScope();
            var reconciler = scope.ServiceProvider.GetRequiredService<DraftSendReconciler>();
            try
            {
                await reconciler.ReconcileAsync(draft.TenantId, draft.DraftId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Un borrador que falla no corta el resto: sigue en Sending para el próximo barrido.
                logger.LogError(ex, "Could not reconcile draft {DraftId}.", draft.DraftId);
            }
        }
    }
}
