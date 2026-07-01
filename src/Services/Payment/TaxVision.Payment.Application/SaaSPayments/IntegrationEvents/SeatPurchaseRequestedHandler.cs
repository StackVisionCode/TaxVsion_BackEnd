using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Domain.SaaSPayments;
using TaxVision.Payment.Domain.StripeCustomers;
using Wolverine;

namespace TaxVision.Payment.Application.SaaSPayments.IntegrationEvents;

public static class SeatPurchaseRequestedHandler
{
    public static async Task Handle(
        SeatPurchaseRequestedIntegrationEvent evt,
        ISaaSPaymentRepository payments,
        IStripeCustomerRepository stripeCustomers,
        IStripeGateway stripe,
        IUnitOfWork uow,
        IMessageBus bus,
        CancellationToken ct)
    {
        var existing = await stripeCustomers.GetByTenantIdAsync(evt.TenantId, ct);
        string stripeCustomerId;
        if (existing is null)
        {
            stripeCustomerId = await stripe.GetOrCreateCustomerAsync(evt.TenantId, $"tenant-{evt.TenantId}@taxvision.internal", ct);
            var newCustomer = StripeCustomer.Create(evt.TenantId, stripeCustomerId, $"tenant-{evt.TenantId}@taxvision.internal");
            await stripeCustomers.AddAsync(newCustomer, ct);
        }
        else
        {
            stripeCustomerId = existing.StripeCustomerId;
        }

        var payment = SaaSPayment.Create(
            evt.TenantId,
            SaaSPaymentType.SeatPurchase,
            evt.AmountCents,
            evt.Currency,
            evt.SeatSubscriptionId);
        await payments.AddAsync(payment, ct);
        await uow.SaveChangesAsync(ct);

        var description = $"Seat purchase ({evt.Quantity} seats) for subscription {evt.SubscriptionId}";
        var intentResult = await stripe.CreatePaymentIntentAsync(stripeCustomerId, evt.AmountCents, evt.Currency, description, ct);
        if (!intentResult.IsSuccess || intentResult.PaymentIntentId is null)
        {
            payment.MarkFailed(intentResult.FailureReason ?? "Failed to create payment intent.");
            await uow.SaveChangesAsync(ct);
            await bus.PublishAsync(new SeatPaymentFailedIntegrationEvent
            {
                TenantId = evt.TenantId,
                CorrelationId = evt.CorrelationId,
                SeatSubscriptionId = evt.SeatSubscriptionId,
                Reason = payment.FailureReason!
            });
            return;
        }

        payment.MarkProcessing(intentResult.PaymentIntentId);
        await uow.SaveChangesAsync(ct);

        var confirmResult = await stripe.ConfirmPaymentIntentAsync(intentResult.PaymentIntentId, ct);
        if (confirmResult.IsSuccess)
        {
            payment.MarkCompleted();
            await uow.SaveChangesAsync(ct);
            await bus.PublishAsync(new SeatPaymentCompletedIntegrationEvent
            {
                TenantId = evt.TenantId,
                CorrelationId = evt.CorrelationId,
                SeatSubscriptionId = evt.SeatSubscriptionId
            });
        }
        else
        {
            payment.MarkFailed(confirmResult.FailureReason ?? "Payment confirmation failed.");
            await uow.SaveChangesAsync(ct);
            await bus.PublishAsync(new SeatPaymentFailedIntegrationEvent
            {
                TenantId = evt.TenantId,
                CorrelationId = evt.CorrelationId,
                SeatSubscriptionId = evt.SeatSubscriptionId,
                Reason = payment.FailureReason!
            });
        }
    }
}
