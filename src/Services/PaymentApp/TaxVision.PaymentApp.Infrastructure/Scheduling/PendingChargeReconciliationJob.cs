using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ReconcilePendingSaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ReconcileStuckSaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using Wolverine;

namespace TaxVision.PaymentApp.Infrastructure.Scheduling;

/// <summary>
/// Resuelve pagos atascados en <see cref="PaymentStatus.Processing"/> tras una caída de
/// PaymentApp a mitad de cobro (§1714 del diseño). Solo busca candidatos y despacha un
/// <see cref="ReconcileStuckSaaSPaymentCommand"/> por pago, igual que <see cref="DunningJob"/>:
/// cada comando consulta al provider, guarda y publica en su propia transacción con outbox.
/// También cierra los cobros <see cref="PaymentStatus.Pending"/> cuyo comando se perdió
/// (<see cref="ReconcilePendingSaaSPaymentCommand"/>).
/// </summary>
public sealed class PendingChargeReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<PendingChargeReconciliationJob> logger
) : PeriodicPaymentAppJob(scopeFactory, lockFactory, logger, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(50))
{
    private const int BatchSize = 100;

    // Cadencia corta a propósito: un cobro que el webhook ya confirmó pasa a Succeeded y NUNCA entra en
    // este barrido, así que preguntar seguido no cuesta llamadas de más — solo alcanza a los cobros que de
    // verdad siguen sin confirmar.
    private static readonly TimeSpan StuckThreshold = TimeSpan.FromSeconds(45);

    protected override string JobName => "pending-charge-reconciliation";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var payments = services.GetRequiredService<ISaaSPaymentRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var logger = services.GetRequiredService<ILogger<PendingChargeReconciliationJob>>();

        var stuck = await payments.GetStuckProcessingIdsAsync(DateTime.UtcNow - StuckThreshold, BatchSize, ct);

        var failures = 0;
        foreach (var (paymentId, tenantId) in stuck)
        {
            try
            {
                // El onboarding no tiene tenant todavía: el handler lo lee sin filtro de tenant.
                bus.TenantId = tenantId == Guid.Empty ? null : tenantId.ToString();
                var result = await bus.InvokeAsync<Result>(
                    new ReconcileStuckSaaSPaymentCommand(tenantId, paymentId),
                    ct
                );

                if (result.IsFailure)
                {
                    failures++;
                    logger.LogWarning(
                        "Reconciliation failed for stuck SaaSPayment {SaaSPaymentId}: {Code} — {Message}",
                        paymentId,
                        result.Error.Code,
                        result.Error.Message
                    );
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Un pago que falla (p. ej. choque con el webhook) no frena a los demás: vuelve en la próxima corrida.
                failures++;
                logger.LogError(ex, "Reconciliation threw for stuck SaaSPayment {SaaSPaymentId}.", paymentId);
            }
        }

        if (stuck.Count > 0)
            logger.LogInformation(
                "PendingChargeReconciliationJob examined {Total} stuck payment(s), {Failures} failed.",
                stuck.Count,
                failures
            );

        await ReconcileStalePendingAsync(services, payments, bus, logger, ct);
    }

    private static async Task ReconcileStalePendingAsync(
        IServiceProvider services,
        ISaaSPaymentRepository payments,
        IMessageBus bus,
        ILogger logger,
        CancellationToken ct
    )
    {
        var threshold = services.GetRequiredService<IOptions<SaaSPaymentChargeOptions>>().Value.PendingChargeThreshold;
        var stale = await payments.GetStalePendingChargeIdsAsync(DateTime.UtcNow - threshold, BatchSize, ct);

        foreach (var (paymentId, tenantId) in stale)
        {
            try
            {
                bus.TenantId = tenantId.ToString();
                var result = await bus.InvokeAsync<Result>(
                    new ReconcilePendingSaaSPaymentCommand(tenantId, paymentId),
                    ct
                );
                if (result.IsFailure)
                    logger.LogWarning(
                        "Reconciliation failed for pending SaaSPayment {SaaSPaymentId}: {Code} — {Message}",
                        paymentId,
                        result.Error.Code,
                        result.Error.Message
                    );
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Choque con el cobro en curso u otro fallo: vuelve en la próxima corrida.
                logger.LogError(ex, "Reconciliation threw for pending SaaSPayment {SaaSPaymentId}.", paymentId);
            }
        }

        if (stale.Count > 0)
            logger.LogInformation(
                "PendingChargeReconciliationJob examined {Total} stale pending charge(s).",
                stale.Count
            );
    }
}
