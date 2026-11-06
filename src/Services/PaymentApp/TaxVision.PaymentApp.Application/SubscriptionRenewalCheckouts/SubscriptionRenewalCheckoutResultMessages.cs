using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.SaaSPayments.Events;

namespace TaxVision.PaymentApp.Application.SubscriptionRenewalCheckouts;

/// <summary>Aviso del resultado de un pago de checkout de renovación self-service
/// (<see cref="SaaSPaymentType.SubscriptionRenewalCheckout"/>). Molde: <c>SeatsCheckoutResultMessages</c>.</summary>
public static class SubscriptionRenewalCheckoutResultMessages
{
    public static object? ResultOf(SaaSPaymentSnapshot payment, string correlationId)
    {
        if (payment.Status == PaymentStatus.Succeeded)
            return new SubscriptionRenewalCheckoutPaidIntegrationEvent
            {
                TenantId = payment.TenantId,
                RenewalIntentId = payment.TargetAggregateId,
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
        return new SubscriptionRenewalCheckoutFailedIntegrationEvent
        {
            TenantId = payment.TenantId,
            RenewalIntentId = payment.TargetAggregateId,
            SaaSPaymentId = payment.SaaSPaymentId,
            FailureCode = payment.FailureCode ?? (cancelled ? "Provider.Cancelled" : "Unknown"),
            FailureReason = payment.FailureReason ?? (cancelled ? "The payment was cancelled." : "The charge failed."),
            CorrelationId = correlationId,
        };
    }
}
