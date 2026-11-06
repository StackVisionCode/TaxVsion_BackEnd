using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.SaaSPayments.Events;

namespace TaxVision.PaymentApp.Application.SeatsCheckouts;

/// <summary>Aviso del resultado de un pago de checkout de asientos (<see cref="SaaSPaymentType.SeatsPurchaseCharge"/>).
/// Molde: el resultado del onboarding, pero con el tenant REAL del pago (los asientos sí tienen tenant).</summary>
public static class SeatsCheckoutResultMessages
{
    public static object? ResultOf(SaaSPaymentSnapshot payment, string correlationId)
    {
        if (payment.Status == PaymentStatus.Succeeded)
            return new SeatsCheckoutPaidIntegrationEvent
            {
                TenantId = payment.TenantId,
                SeatPurchaseIntentId = payment.TargetAggregateId,
                SaaSPaymentId = payment.SaaSPaymentId,
                AmountPaidCents = payment.AmountCents,
                Currency = payment.Currency,
                PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
                ProviderPaymentReference = payment.ExternalChargeReference ?? string.Empty,
                CorrelationId = correlationId,
            };

        if (payment.Status is not (PaymentStatus.Failed or PaymentStatus.Cancelled))
            return null;

        var cancelled = payment.Status == PaymentStatus.Cancelled;
        return new SeatsCheckoutFailedIntegrationEvent
        {
            TenantId = payment.TenantId,
            SeatPurchaseIntentId = payment.TargetAggregateId,
            SaaSPaymentId = payment.SaaSPaymentId,
            FailureCode = payment.FailureCode ?? (cancelled ? "Provider.Cancelled" : "Unknown"),
            FailureReason = payment.FailureReason ?? (cancelled ? "The payment was cancelled." : "The charge failed."),
            CorrelationId = correlationId,
        };
    }
}
