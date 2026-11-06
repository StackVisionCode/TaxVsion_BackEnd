using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.TenantPayments.Common;
using TaxVision.PaymentClient.Domain.TenantPayments.Events;
using Wolverine;

namespace TaxVision.PaymentClient.Application.TenantPayments.DomainEventHandlers;

/// <summary>
/// El pago llegó a Succeeded: devuelve la señal "pagado" (cascada por el outbox hacia Billing y Notification).
/// Corre después del commit, desde la cola domain-events. Relee el pago con el tenant explícito; la hora del pago
/// sale del evento.
/// </summary>
public static class TenantPaymentSucceededDomainEventHandler
{
    public static async Task<TenantPaymentSucceededIntegrationEvent> Handle(
        TenantPaymentSucceededDomainEvent succeeded,
        ITenantPaymentRepository payments,
        Envelope envelope,
        CancellationToken ct
    )
    {
        // El pago se guardó en la misma transacción que este evento: si no está, es un error y va a reintento/DLQ.
        var payment =
            await payments.GetByIdAsync(succeeded.TenantPaymentId, succeeded.TenantId, ct)
            ?? throw new InvalidOperationException(
                $"TenantPayment {succeeded.TenantPaymentId} of tenant {succeeded.TenantId} does not exist."
            );

        return TenantPaymentSucceededSignal.Build(payment, succeeded.PaidAtUtc, envelope.CorrelationId);
    }
}
