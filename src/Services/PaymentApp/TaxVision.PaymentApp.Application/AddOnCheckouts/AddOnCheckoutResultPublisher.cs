using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using Wolverine;

namespace TaxVision.PaymentApp.Application.AddOnCheckouts;

/// <summary>Publica el resultado de un pago de checkout de add-on (<see cref="SaaSPaymentType.AddOnPurchaseCharge"/>)
/// tras aplicarse un webhook. Molde: <c>SeatsCheckoutResultPublisher</c>.</summary>
public static class AddOnCheckoutResultPublisher
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
                new AddOnCheckoutPaidIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    AddOnPurchaseIntentId = payment.TargetAggregateId,
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
                new AddOnCheckoutFailedIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    AddOnPurchaseIntentId = payment.TargetAggregateId,
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
