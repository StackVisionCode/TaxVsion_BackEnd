using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.Onboarding.IntegrationEvents;

/// <summary>PayFlow (Fase 17) — consume <see cref="OnboardingRefundRequestedIntegrationEvent"/>
/// (publicado por Auth's <c>CancelAndRefundOnboardingAdminHandler</c> tras una acción admin
/// explícita con confirmación textual). Carga el <see cref="SaaSPayment"/> directo por
/// <c>PaymentId</c> (ver doc-comment del evento) y aplica el mismo patrón de
/// <c>RefundSaaSPaymentHandler</c>: provider primero, dominio después — nunca al revés, para no
/// dejar el aggregate en <c>Refunded</c> si Stripe rechaza el refund.</summary>
public static class OnboardingRefundConsumer
{
    public static async Task Handle(
        OnboardingRefundRequestedIntegrationEvent evt,
        ISaaSPaymentRepository payments,
        IPaymentAdapterFactory providerFactory,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
        );

        var payment = await payments.GetByIdAsync(evt.PaymentId, ct);
        if (payment is null)
        {
            logger.LogWarning(
                "OnboardingRefundConsumer: SaaSPayment {PaymentId} for onboarding {OnboardingId} not found — nothing to refund.",
                evt.PaymentId,
                evt.OnboardingId
            );
            return;
        }

        if (payment.ExternalChargeReference is null)
        {
            logger.LogWarning(
                "OnboardingRefundConsumer: SaaSPayment {PaymentId} has no charge reference — nothing was ever charged.",
                payment.Id
            );
            return;
        }

        var adapter = providerFactory.Resolve(payment.ProviderCode);
        var refundResult = await adapter.RefundAsync(
            payment.ExternalChargeReference.Value,
            payment.Amount,
            evt.Reason,
            ct
        );
        if (refundResult.IsFailure)
        {
            logger.LogWarning(
                "OnboardingRefundConsumer: provider refund failed for SaaSPayment {PaymentId} (onboarding {OnboardingId}): {Error}",
                payment.Id,
                evt.OnboardingId,
                refundResult.Error.Message
            );
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var applyResult = payment.RefundFull(evt.Reason, Guid.Empty, nowUtc);
        if (applyResult.IsFailure)
        {
            logger.LogWarning(
                "OnboardingRefundConsumer: provider refund succeeded but domain apply failed for SaaSPayment {PaymentId}: {Error}",
                payment.Id,
                applyResult.Error.Message
            );
            return;
        }

        metrics.RecordRefunded(payment.ProviderCode.ToString());

        await AuditEntryFactory.AppendAsync(
            audit,
            payment.TenantId,
            nameof(SaaSPayment),
            payment.Id,
            PaymentAuditAction.SaaSPaymentRefundedFull,
            Guid.Empty,
            correlation.CorrelationId,
            before: (object?)null,
            after: new { payment.Status, RefundedCents = payment.Amount.AmountCents },
            reason: evt.Reason,
            nowUtc,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "OnboardingRefundConsumer: SaaSPayment {PaymentId} for onboarding {OnboardingId} refunded in full; status now {Status}.",
            payment.Id,
            evt.OnboardingId,
            payment.Status
        );
    }
}
