using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Infrastructure.Scheduling;

/// <summary>
/// Resuelve pagos atascados en <see cref="PaymentStatus.Processing"/> tras una caída de
/// PaymentApp a mitad de cobro (§1714 del diseño). Consulta al provider como confirmación
/// out-of-band vía <see cref="IPaymentProvider.GetChargeStatusAsync"/> — nunca asume nada
/// sobre un cobro que no terminó de confirmarse localmente.
/// </summary>
public sealed class PendingChargeReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<PendingChargeReconciliationJob> logger
) : PeriodicPaymentAppJob(scopeFactory, lockFactory, logger, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(50))
{
    private const int BatchSize = 100;
    private static readonly TimeSpan ProviderNotFoundGrace = TimeSpan.FromMinutes(30);

    // Cadencia corta a propósito: un cobro que el webhook ya confirmó pasa a Succeeded y NUNCA entra en
    // este barrido, así que preguntar seguido no cuesta llamadas de más — solo alcanza a los cobros que de
    // verdad siguen sin confirmar. Con el umbral y el intervalo anteriores (5 min cada uno) el peor caso
    // eran 10 minutos sólo en este paso, y Subscription sumaba los suyos encima.
    private static readonly TimeSpan StuckThreshold = TimeSpan.FromSeconds(45);

    protected override string JobName => "pending-charge-reconciliation";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var payments = services.GetRequiredService<ISaaSPaymentRepository>();
        var providerFactory = services.GetRequiredService<IPaymentAdapterFactory>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var logger = services.GetRequiredService<ILogger<PendingChargeReconciliationJob>>();

        var cutoffUtc = DateTime.UtcNow - StuckThreshold;
        var stuck = await payments.GetStuckProcessingAsync(cutoffUtc, BatchSize, ct);

        var bus = services.GetRequiredService<IMessageBus>();
        var correlation = services.GetRequiredService<ICorrelationContext>();
        // El recibo lleva el nombre de la oficina, y este camino también lo emite: sin el registro
        // saldría en blanco justo cuando el webhook no llegó, que es cuando más se usa.
        var tenants = services.GetRequiredService<ITenantRegistry>();

        var resolvedCount = 0;
        foreach (var payment in stuck)
        {
            using (correlation.Push(Guid.NewGuid().ToString("N")))
            {
                if (
                    await TryResolveAsync(payment, providerFactory, bus, tenants, correlation.CorrelationId, logger, ct)
                )
                    resolvedCount++;
            }
        }

        if (stuck.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "PendingChargeReconciliationJob examined {Total} stuck payment(s), resolved {Resolved}.",
                stuck.Count,
                resolvedCount
            );
        }
    }

    private static async Task<bool> TryResolveAsync(
        SaaSPayment payment,
        IPaymentAdapterFactory providerFactory,
        IMessageBus bus,
        ITenantRegistry tenants,
        string correlationId,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (payment.ExternalChargeReference is null)
            return false;

        var adapter = providerFactory.Resolve(payment.ProviderCode);
        // Cualquier pago por HOSTED-CHECKOUT guarda como referencia el id de la SESIÓN (cs_...), no el del
        // PaymentIntent — así que hay que FINALIZAR la sesión (resolverla a su cargo real) para conocer su
        // estado; GetChargeStatusAsync espera un PaymentIntent y no confirma una sesión. Aplica a onboarding,
        // seats y renovación self-service por igual (antes solo onboarding lo hacía, dejando a los otros dos
        // sin reconciliar cuando el webhook no llegaba — el gap que reveló el pago real de renovación).
        Result<ChargeAuthorizationResult> statusResult;
        try
        {
            statusResult = !string.IsNullOrWhiteSpace(payment.ProviderCheckoutSessionId)
                ? await adapter.FinalizeHostedCheckoutAsync(payment.ExternalChargeReference.Value, payment.Amount, ct)
                : await adapter.GetChargeStatusAsync(payment.ExternalChargeReference.Value, ct);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex, ct))
        {
            logger.LogWarning(
                ex,
                "Could not confirm status for stuck SaaSPayment {SaaSPaymentId}: provider request failed.",
                payment.Id
            );
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        if (statusResult.IsFailure)
        {
            if (ShouldFailProviderNotFound(payment, statusResult.Error, nowUtc))
            {
                var notFound = payment.MarkFailed(
                    statusResult.Error.Code,
                    statusResult.Error.Message,
                    willRetry: false,
                    nextRetryAtUtc: null,
                    Guid.Empty,
                    nowUtc
                );

                if (notFound.IsSuccess)
                    await SaaSPaymentResultPublisher.PublishAsync(payment, bus, correlationId, ct, tenants);

                return notFound.IsSuccess;
            }

            logger.LogWarning(
                "Could not confirm status for stuck SaaSPayment {SaaSPaymentId}: {Error}",
                payment.Id,
                statusResult.Error.Message
            );
            return false;
        }

        var outcome = statusResult.Value;

        // Igual criterio que el webhook checkout.session.completed (ver StripePaymentAdapter):
        // si el provider ya resolvió una referencia más autoritativa que la guardada (p.ej. el
        // PaymentIntent real detrás de una Checkout Session), se reconcilia acá también -- así
        // refund/dispute futuros pueden resolver este pago sin depender de que el webhook haya
        // llegado.
        if (outcome.ProviderChargeReference != payment.ExternalChargeReference.Value)
        {
            var referenceResult = ExternalPaymentReference.Create(
                payment.ProviderCode,
                outcome.ProviderChargeReference
            );
            if (referenceResult.IsSuccess)
                payment.ReconcileProviderChargeReference(referenceResult.Value, nowUtc);
        }

        switch (outcome.Status)
        {
            case PaymentStatus.Succeeded:
                var succeeded = payment.MarkSucceeded(nowUtc, Guid.Empty);
                if (succeeded.IsSuccess)
                    await SaaSPaymentResultPublisher.PublishAsync(payment, bus, correlationId, ct, tenants);
                return succeeded.IsSuccess;

            case PaymentStatus.Failed
            or PaymentStatus.Cancelled:
                var nextRetryAtUtc = SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(
                    payment,
                    nowUtc,
                    failedAttemptRecorded: true
                );
                var failed = payment.MarkFailed(
                    outcome.FailureCode ?? "Provider.Unknown",
                    outcome.FailureMessage ?? "The provider reported the charge as failed.",
                    willRetry: nextRetryAtUtc is not null,
                    nextRetryAtUtc,
                    Guid.Empty,
                    nowUtc
                );
                if (failed.IsSuccess)
                    await SaaSPaymentResultPublisher.PublishAsync(payment, bus, correlationId, ct);
                return failed.IsSuccess;

            default:
                // Sigue Processing/RequiresAction del lado del provider — nada que hacer,
                // se reintenta en la próxima corrida.
                return false;
        }
    }

    private static bool ShouldFailProviderNotFound(SaaSPayment payment, Error error, DateTime nowUtc) =>
        IsProviderNotFound(error) && payment.UpdatedAtUtc <= nowUtc - ProviderNotFoundGrace;

    private static bool IsProviderNotFound(Error error) =>
        error.Code.EndsWith(".ChargeStatus.NotFound", StringComparison.Ordinal)
        || error.Message.Contains("HTTP 404", StringComparison.OrdinalIgnoreCase);

    private static bool IsProviderTransportFailure(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested && ex is OperationCanceledException or HttpRequestException or TimeoutException;
}
