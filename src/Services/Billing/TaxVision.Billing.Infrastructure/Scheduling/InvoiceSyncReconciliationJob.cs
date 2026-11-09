using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.SyncReconciliation;
using TaxVision.Billing.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.Billing.Infrastructure.Scheduling;

/// <summary>
/// BIL-3b: cada <see cref="InvoiceSyncReconciliationOptions.Interval"/> vuelve a publicar
/// <c>EnsureInvoicePaymentLinkCommand</c> y <c>SyncInvoiceStockCommand</c> para las facturas cuya marca de
/// sincronización no coincide con su estado actual (por ejemplo, un aviso que terminó en la cola de errores).
/// Los dos comandos son idempotentes. Una factura por unidad de outbox; candado para una sola réplica.
/// </summary>
public sealed class InvoiceSyncReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    IOptions<InvoiceSyncReconciliationOptions> options,
    TimeProvider clock,
    ILogger<InvoiceSyncReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            return;

        // Sin esperar el arranque, publicar puede ganarle a Wolverine (WolverineHasNotStartedException).
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        using var timer = new PeriodicTimer(settings.Interval);
        do
        {
            try
            {
                await RunOnceAsync(settings, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Invoice sync reconciliation iteration failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(InvoiceSyncReconciliationOptions settings, CancellationToken ct)
    {
        if (settings.NotBeforeUtc is not { } notBefore)
        {
            logger.LogWarning("Invoice sync reconciliation skipped: NotBeforeUtc is not configured.");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        await using var handle = await scope
            .ServiceProvider.GetRequiredService<SqlInvoiceSyncReconciliationLock>()
            .TryAcquireAsync(ct);
        if (handle is null)
            return;

        var now = clock.GetUtcNow().UtcDateTime;
        var window = new InvoiceSyncWindow(DateTime.SpecifyKind(notBefore, DateTimeKind.Utc), settings.Grace);
        var candidates = await scope
            .ServiceProvider.GetRequiredService<IInvoiceSyncCandidateQuery>()
            .ListAsync(window.NotBeforeUtc, now - window.Grace, settings.BatchSize, ct);

        var republished = 0;
        foreach (var invoice in candidates)
        {
            var commands = InvoiceSyncPolicy.PlanFor(invoice, now, window);
            if (commands.Count == 0)
                continue;

            using var unit = scopeFactory.CreateScope();
            var outbox = unit.ServiceProvider.GetRequiredService<IDbContextOutbox<BillingDbContext>>();
            foreach (var command in commands)
                await outbox.PublishAsync(command);
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
            republished++;
        }

        if (republished > 0)
            logger.LogWarning("Invoice sync reconciliation republished commands for {Count} invoice(s).", republished);
    }
}
