using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SubscriptionRenewalCheckouts;

/// <summary>Publica el resultado de un pago de checkout de renovación self-service
/// (<see cref="SaaSPaymentType.SubscriptionRenewalCheckout"/>) tras aplicarse un webhook. Molde:
/// <c>SeatsCheckoutResultPublisher</c>.</summary>
public static class SubscriptionRenewalCheckoutResultPublisher
{
    public static async ValueTask PublishAsync(
        SaaSPayment payment,
        IMessageBus bus,
        string correlationId,
        CancellationToken ct
    )
    {
        if (payment.Status == PaymentStatus.Succeeded)
        {
            await bus.PublishAsync(
                new SubscriptionRenewalCheckoutPaidIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    RenewalIntentId = payment.TargetAggregateId,
                    SaaSPaymentId = payment.Id,
                    AmountPaidCents = payment.Amount.AmountCents,
                    Currency = payment.Amount.Currency,
                    PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
                    ProviderPaymentReference = payment.ExternalChargeReference?.Value ?? string.Empty,
                    CorrelationId = correlationId,
                }
            );
        }
        else if (payment.Status is PaymentStatus.Failed or PaymentStatus.Cancelled)
        {
            var cancelled = payment.Status == PaymentStatus.Cancelled;
            await bus.PublishAsync(
                new SubscriptionRenewalCheckoutFailedIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    RenewalIntentId = payment.TargetAggregateId,
                    SaaSPaymentId = payment.Id,
                    FailureCode = payment.FailureCode ?? (cancelled ? "Provider.Cancelled" : "Unknown"),
                    FailureReason =
                        payment.FailureReason ?? (cancelled ? "The payment was cancelled." : "The charge failed."),
                    CorrelationId = correlationId,
                }
            );
        }
    }
}
