using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.RefundSaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.PaymentApp.Application.Onboarding.IntegrationEvents;

/// <summary>PayFlow (Fase 17) — consume <see cref="OnboardingRefundRequestedIntegrationEvent"/> (lo publica Auth tras
/// la acción admin confirmada) y reembolsa el pago completo con el patrón BB-8: intención guardada, proveedor fuera de
/// la transacción con la clave <c>refund:onboarding:{id}</c> y registro aparte. Nada se pierde:
/// <list type="bullet">
/// <item>fallo pasajero del proveedor: excepción no permanente, la política estándar reintenta y deja el mensaje en la
/// cola de errores;</item>
/// <item>rechazo definitivo: auditado y el pago en la bandeja de revisión (<c>RefundRejected</c>);</item>
/// <item>reembolsado sin registro: auditado, en revisión (<c>RefundNeedsReconciliation</c>) y nunca se reenvía.</item>
/// </list>
/// Si el pago no puede entrar a la bandeja (ya tenía una revisión resuelta), el mensaje va a la cola de errores.</summary>
public static class OnboardingRefundConsumer
{
    /// <summary>La única petición de reembolso de onboarding por pago.</summary>
    public const string RequestKey = "onboarding";

    [NonTransactional]
    public static async Task Handle(
        OnboardingRefundRequestedIntegrationEvent evt,
        IMessageBus bus,
        IPaymentAdapterFactory providerFactory,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
        );

        var prepared = await bus.InvokeAsync<Result<SaaSPaymentRefundPlan>>(
            new PrepareSaaSPaymentRefundCommand(
                evt.PaymentId,
                AmountCents: null,
                evt.Reason,
                Guid.Empty,
                RequestKey,
                $"refund:onboarding:{evt.PaymentId:N}",
                ReplayWindow: null,
                EnforceAdminThrottle: false
            ),
            ct
        );
        if (prepared.IsFailure)
        {
            HandlePrepareFailure(evt, prepared.Error, logger);
            return;
        }

        var plan = prepared.Value;
        if (plan.Kind != SaaSPaymentRefundPlanKind.Execute)
        {
            logger.LogWarning(
                "OnboardingRefundConsumer: refund of SaaSPayment {PaymentId} (onboarding {OnboardingId}) is {Kind}; the provider is not called again.",
                evt.PaymentId,
                evt.OnboardingId,
                plan.Kind
            );
            return;
        }

        var adapter = providerFactory.Resolve(plan.Provider);
        var refund = await adapter.RefundAsync(plan.ChargeReference, plan.Amount, plan.Reason, plan.ProviderKey, ct);

        RecordSaaSPaymentRefundCommand record;
        if (refund.IsSuccess)
            record = new(evt.PaymentId, plan.RequestId, refund.Value.ProviderRefundReference);
        else if (!ProviderRefundFailures.IsTransient(refund.Error))
            record = new(evt.PaymentId, plan.RequestId, null, refund.Error.Code, FlagRejectionForReview: true);
        else if (adapter.Capabilities.SupportsIdempotencyKeys)
            // La intención sigue abierta: el reintento repite la misma clave.
            throw new SaaSPaymentRefundRetryableException(
                $"Onboarding refund for SaaSPayment {evt.PaymentId} is temporarily unavailable: {refund.Error.Code}."
            );
        else
            record = new(evt.PaymentId, plan.RequestId, null, Unrecorded: true);

        var recorded = await bus.InvokeAsync<Result<SaaSPaymentRefundRecord>>(record, ct);
        if (recorded.IsFailure)
            throw new SaaSPaymentRefundRetryableException(
                $"Onboarding refund for SaaSPayment {evt.PaymentId} could not be recorded: {recorded.Error.Code}."
            );

        if (recorded.Value.Kind != SaaSPaymentRefundRecordKind.Recorded && !recorded.Value.VisibleForReview)
            // Ya auditado; sin bandeja que lo muestre, la cola de errores lo hace visible.
            throw new ArgumentException(
                $"Onboarding refund for SaaSPayment {evt.PaymentId} is {recorded.Value.Kind} and the payment cannot be flagged for review."
            );

        logger.LogInformation(
            "OnboardingRefundConsumer: SaaSPayment {PaymentId} for onboarding {OnboardingId}: {Kind}.",
            evt.PaymentId,
            evt.OnboardingId,
            recorded.Value.Kind
        );
    }

    private static void HandlePrepareFailure(OnboardingRefundRequestedIntegrationEvent evt, Error error, ILogger logger)
    {
        switch (error.Code)
        {
            // Sin pago o sin cobro: no hay nada que devolver.
            case "SaaSPayment.NotFound":
            case "SaaSPayment.NoChargeReference":
                logger.LogWarning(
                    "OnboardingRefundConsumer: SaaSPayment {PaymentId} for onboarding {OnboardingId}: {Code} — nothing to refund.",
                    evt.PaymentId,
                    evt.OnboardingId,
                    error.Code
                );
                return;
            // Otro reembolso del mismo pago sigue sin resultado: se reintenta más tarde.
            case "SaaSPayment.RefundInProgress":
                throw new SaaSPaymentRefundRetryableException(
                    $"Onboarding refund for SaaSPayment {evt.PaymentId} waits for another refund in progress."
                );
            // No procede (retención legal, estado, monto): a la cola de errores, no se pierde en silencio.
            default:
                throw new ArgumentException(
                    $"Onboarding refund for SaaSPayment {evt.PaymentId} rejected: {error.Code}."
                );
        }
    }
}
