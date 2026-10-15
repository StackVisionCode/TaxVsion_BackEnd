using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;

/// <summary>
/// Propaga la cancelación / expiración de un intent de compra (seat / add-on / renewal / plan-change-upgrade)
/// desde Subscription hacia el <see cref="SaaSPayment"/> en PaymentApp. Sin esto, el flujo del landing
/// "Cancel and Start Over" dejaba el pago en Failed porque Stripe envía <c>checkout.session.expired</c>
/// cuando se cancela la sesión y el adapter lo traduce a <see cref="PaymentStatus.Failed"/>. Al cancelar
/// el pago local primero, el webhook posterior queda stale y el historial muestra "Cancelled".
/// Idempotente ante redeliveries por <see cref="SaaSPayment.MarkCancelled"/>.
/// </summary>
public static class CheckoutCancelledOrExpiredSaaSPaymentConsumer
{
    public static Task Handle(
        SeatCheckoutCancelledIntegrationEvent evt,
        ISaaSPaymentRepository payments,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    ) =>
        ApplyAsync(
            evt.SaaSPaymentId,
            evt.TenantId,
            evt.CorrelationId,
            evt.EventId,
            "BuyerCancelled",
            payments,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    public static Task Handle(
        SeatCheckoutExpiredIntegrationEvent evt,
        ISaaSPaymentRepository payments,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    ) =>
        ApplyAsync(
            evt.SaaSPaymentId,
            evt.TenantId,
            evt.CorrelationId,
            evt.EventId,
            "IntentExpired",
            payments,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    public static Task Handle(
        AddOnCheckoutCancelledIntegrationEvent evt,
        ISaaSPaymentRepository payments,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    ) =>
        ApplyAsync(
            evt.SaaSPaymentId,
            evt.TenantId,
            evt.CorrelationId,
            evt.EventId,
            "BuyerCancelled",
            payments,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    public static Task Handle(
        AddOnCheckoutExpiredIntegrationEvent evt,
        ISaaSPaymentRepository payments,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    ) =>
        ApplyAsync(
            evt.SaaSPaymentId,
            evt.TenantId,
            evt.CorrelationId,
            evt.EventId,
            "IntentExpired",
            payments,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    public static Task Handle(
        SubscriptionRenewalCheckoutCancelledIntegrationEvent evt,
        ISaaSPaymentRepository payments,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    ) =>
        ApplyAsync(
            evt.SaaSPaymentId,
            evt.TenantId,
            evt.CorrelationId,
            evt.EventId,
            "BuyerCancelled",
            payments,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    public static Task Handle(
        SubscriptionRenewalCheckoutExpiredIntegrationEvent evt,
        ISaaSPaymentRepository payments,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    ) =>
        ApplyAsync(
            evt.SaaSPaymentId,
            evt.TenantId,
            evt.CorrelationId,
            evt.EventId,
            "IntentExpired",
            payments,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    public static Task Handle(
        PlanChangeUpgradeCancelledIntegrationEvent evt,
        ISaaSPaymentRepository payments,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    ) =>
        ApplyAsync(
            evt.SaaSPaymentId,
            evt.TenantId,
            evt.CorrelationId,
            evt.EventId,
            "BuyerCancelled",
            payments,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    public static Task Handle(
        PlanChangeUpgradeExpiredIntegrationEvent evt,
        ISaaSPaymentRepository payments,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    ) =>
        ApplyAsync(
            evt.SaaSPaymentId,
            evt.TenantId,
            evt.CorrelationId,
            evt.EventId,
            "IntentExpired",
            payments,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    private static async Task ApplyAsync(
        Guid? saaSPaymentId,
        Guid tenantId,
        string? correlationIdFromEvent,
        Guid eventId,
        string reason,
        ISaaSPaymentRepository payments,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        // Intent sin SaaSPayment (p.ej. una compra que nunca llegó al provider): no hay nada que marcar.
        if (saaSPaymentId is not { } paymentId || paymentId == Guid.Empty)
            return;

        var correlationId = string.IsNullOrWhiteSpace(correlationIdFromEvent)
            ? eventId.ToString("N")
            : correlationIdFromEvent;

        using (correlation.Push(correlationId))
        {
            var payment = await payments.GetByIdAsync(paymentId, tenantId, ct);
            if (payment is null)
            {
                logger.LogWarning(
                    "SaaSPayment {PaymentId} not found for tenant {TenantId}; cannot mark it cancelled.",
                    paymentId,
                    tenantId
                );
                return;
            }

            // Redelivery: ya era Cancelled, no hay nada que escribir.
            if (payment.Status == PaymentStatus.Cancelled)
                return;

            var result = payment.MarkCancelled(reason, DateTime.UtcNow);
            if (result.IsFailure)
            {
                // Succeeded / Refunded / PartiallyRefunded / ChargedBack: una terminal distinta ya ganó.
                logger.LogInformation(
                    "SaaSPayment {PaymentId} not transitioned to Cancelled from {Status}: {Code}.",
                    payment.Id,
                    payment.Status,
                    result.Error.Code
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation("SaaSPayment {PaymentId} marked Cancelled (reason={Reason}).", payment.Id, reason);
        }
    }
}
