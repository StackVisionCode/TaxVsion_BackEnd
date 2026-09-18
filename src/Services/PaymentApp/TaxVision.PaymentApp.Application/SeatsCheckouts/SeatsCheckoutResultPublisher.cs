using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SeatsCheckouts;

/// <summary>Publica el resultado de un pago de checkout de asientos (<see cref="SaaSPaymentType.SeatsPurchaseCharge"/>)
/// tras aplicarse un webhook. Molde: <c>ProcessStripeWebhookHandler.PublishOnboardingResultAsync</c>, pero con
/// el tenant REAL del pago (los asientos, a diferencia del onboarding, tienen tenant).</summary>
public static class SeatsCheckoutResultPublisher
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
                new SeatsCheckoutPaidIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    SeatPurchaseIntentId = payment.TargetAggregateId,
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
                new SeatsCheckoutFailedIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    SeatPurchaseIntentId = payment.TargetAggregateId,
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
