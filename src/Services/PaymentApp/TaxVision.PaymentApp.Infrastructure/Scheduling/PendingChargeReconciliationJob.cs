using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using BuildingBlocks.Persistence;

namespace TaxVision.PaymentApp.Infrastructure.Scheduling;

/// <summary>
/// Resuelve pagos atascados en <see cref="PaymentStatus.Processing"/> tras una caída de
/// PaymentApp a mitad de cobro (§1714 del diseño). Consulta al provider como confirmación
/// out-of-band vía <see cref="IPaymentProvider.GetChargeStatusAsync"/> — nunca asume nada
/// sobre un cobro que no terminó de confirmarse localmente.
/// </summary>
public sealed class PendingChargeReconciliationJob(
    IServiceScopeFactory scopeFactory, IDistributedLockFactory lockFactory, ILogger<PendingChargeReconciliationJob> logger)
    : PeriodicPaymentAppJob(scopeFactory, lockFactory, logger, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(4))
{
    private const int BatchSize = 100;
    private static readonly TimeSpan StuckThreshold = TimeSpan.FromMinutes(5);

    protected override string JobName => "pending-charge-reconciliation";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var payments = services.GetRequiredService<ISaaSPaymentRepository>();
        var providerFactory = services.GetRequiredService<IPaymentAdapterFactory>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var logger = services.GetRequiredService<ILogger<PendingChargeReconciliationJob>>();

        var cutoffUtc = DateTime.UtcNow - StuckThreshold;
        var stuck = await payments.GetStuckProcessingAsync(cutoffUtc, BatchSize, ct);

        var resolvedCount = 0;
        foreach (var payment in stuck)
        {
            if (await TryResolveAsync(payment, providerFactory, logger, ct))
                resolvedCount++;
        }

        if (stuck.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "PendingChargeReconciliationJob examined {Total} stuck payment(s), resolved {Resolved}.", stuck.Count, resolvedCount);
        }
    }

    private static async Task<bool> TryResolveAsync(
        SaaSPayment payment, IPaymentAdapterFactory providerFactory, ILogger logger, CancellationToken ct)
    {
        if (payment.ExternalChargeReference is null)
            return false;

        var adapter = providerFactory.Resolve(payment.ProviderCode);
        var statusResult = await adapter.GetChargeStatusAsync(payment.ExternalChargeReference.Value, ct);
        if (statusResult.IsFailure)
        {
            logger.LogWarning(
                "Could not confirm status for stuck SaaSPayment {SaaSPaymentId}: {Error}", payment.Id, statusResult.Error.Message);
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        var outcome = statusResult.Value;

        switch (outcome.Status)
        {
            case PaymentStatus.Succeeded:
                var succeeded = payment.MarkSucceeded(nowUtc, Guid.Empty);
                return succeeded.IsSuccess;

            case PaymentStatus.Failed or PaymentStatus.Cancelled:
                var failed = payment.MarkFailed(
                    outcome.FailureCode ?? "Provider.Unknown", outcome.FailureMessage ?? "The provider reported the charge as failed.",
                    willRetry: false, nextRetryAtUtc: null, Guid.Empty, nowUtc);
                return failed.IsSuccess;

            default:
                // Sigue Processing/RequiresAction del lado del provider — nada que hacer,
                // se reintenta en la próxima corrida.
                return false;
        }
    }
}
