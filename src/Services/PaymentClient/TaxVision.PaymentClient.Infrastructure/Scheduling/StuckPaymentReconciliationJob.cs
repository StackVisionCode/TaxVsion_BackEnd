using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ReconcileTenantPayment;
using Wolverine;

namespace TaxVision.PaymentClient.Infrastructure.Scheduling;

/// <summary>
/// Revisión periódica de pagos atascados esperando al proveedor: sin webhook (oficina sin webhook
/// secret, webhook perdido) un <c>Processing</c>/<c>RequiresAction</c> no se resolvería nunca. Cada pago va
/// en su propio <see cref="ReconcileTenantPaymentCommand"/> (su scope y su transacción): uno que falla se
/// registra y se reintenta en otra vuelta sin cortar el lote.
/// </summary>
public sealed class StuckPaymentReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    IOptions<StuckPaymentReconciliationOptions> options,
    ILogger<StuckPaymentReconciliationJob> logger
)
    : PeriodicPaymentClientJob(
        scopeFactory,
        lockFactory,
        logger,
        TimeSpan.FromMinutes(options.Value.IntervalMinutes),
        TimeSpan.FromMinutes(options.Value.IntervalMinutes)
    )
{
    protected override string JobName => "stuck-payment-reconciliation";

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Enabled)
            return base.ExecuteAsync(stoppingToken);

        logger.LogInformation("{JobName} is disabled by configuration.", JobName);
        return Task.CompletedTask;
    }

    protected override Task RunOnceAsync(IServiceProvider services, CancellationToken ct) =>
        ReconcileBatchAsync(
            services.GetRequiredService<ITenantPaymentRepository>(),
            services.GetRequiredService<IMessageBus>(),
            options.Value,
            logger,
            DateTime.UtcNow,
            ct
        );

    /// <summary>Una vuelta: toma el lote y revisa cada pago por separado. Público para probarlo con fakes.</summary>
    public static async Task<StuckPaymentReconciliationSummary> ReconcileBatchAsync(
        ITenantPaymentRepository payments,
        IMessageBus bus,
        StuckPaymentReconciliationOptions settings,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var batch = await payments.GetAwaitingProviderConfirmationAsync(
            nowUtc.AddMinutes(-settings.StuckAfterMinutes),
            nowUtc.AddDays(-settings.GiveUpAfterDays),
            settings.BatchSize,
            ct
        );

        int resolved = 0,
            stillPending = 0,
            failed = 0;
        foreach (var item in batch)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Sin este stamp LocalCommandTenantMiddleware no tiene tenant que restaurar en el scope nuevo.
                bus.TenantId = item.TenantId.ToString();
                var result = await bus.InvokeAsync<Result<TenantPaymentReconciliation>>(
                    new ReconcileTenantPaymentCommand(
                        item.TenantId,
                        item.TenantPaymentId,
                        TimeSpan.FromMinutes(settings.StuckAfterMinutes)
                    ),
                    ct
                );

                if (result.IsFailure)
                {
                    failed++;
                    logger.LogWarning(
                        "Stuck TenantPayment {TenantPaymentId} (tenant {TenantId}) not reconciled: {Code}. Retrying next round.",
                        item.TenantPaymentId,
                        item.TenantId,
                        result.Error.Code
                    );
                }
                else if (result.Value == TenantPaymentReconciliation.Resolved)
                    resolved++;
                else if (result.Value == TenantPaymentReconciliation.StillPending)
                    stillPending++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failed++;
                logger.LogWarning(
                    ex,
                    "Stuck TenantPayment {TenantPaymentId} (tenant {TenantId}) reconciliation threw. Retrying next round.",
                    item.TenantPaymentId,
                    item.TenantId
                );
            }
        }

        var summary = new StuckPaymentReconciliationSummary(batch.Count, resolved, stillPending, failed);
        if (batch.Count > 0)
            logger.LogInformation(
                "Stuck payment reconciliation examined {Examined}: {Resolved} resolved, {StillPending} still pending, {Failed} failed.",
                summary.Examined,
                summary.Resolved,
                summary.StillPending,
                summary.Failed
            );
        return summary;
    }
}

public sealed record StuckPaymentReconciliationSummary(int Examined, int Resolved, int StillPending, int Failed);
