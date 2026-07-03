using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Domain.SaaSPayments;
using TaxVision.Payment.Domain.StripeCustomers;
using Wolverine;

namespace TaxVision.Payment.Application.SaaSPayments.IntegrationEvents;

/// <summary>
/// Wolverine handler for <see cref="SeatRenewalPaymentRequestedIntegrationEvent"/>.
/// Processes a Stripe charge for seat subscription renewals.
/// Publishes <c>SeatRenewalPaymentCompletedIntegrationEvent</c> or <c>SeatRenewalPaymentFailedIntegrationEvent</c>.
/// </summary>
public static class SeatRenewalPaymentRequestedHandler
{
    /// <summary>Processes the seat renewal payment request end-to-end.</summary>
    public static async Task Handle(
        SeatRenewalPaymentRequestedIntegrationEvent evt,
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
            SaaSPaymentType.SeatRenewal,
            evt.AmountCents,
            evt.Currency,
            evt.SeatSubscriptionId);
        await payments.AddAsync(payment, ct);
        await uow.SaveChangesAsync(ct);

        var description = $"Seat renewal for seat subscription {evt.SeatSubscriptionId}";
        var intentResult = await stripe.CreatePaymentIntentAsync(stripeCustomerId, evt.AmountCents, evt.Currency, description, ct);
        if (!intentResult.IsSuccess || intentResult.PaymentIntentId is null)
        {
            payment.MarkFailed(intentResult.FailureReason ?? "Failed to create payment intent.");
            await uow.SaveChangesAsync(ct);
            await bus.PublishAsync(new SeatRenewalPaymentFailedIntegrationEvent
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
            await bus.PublishAsync(new SeatRenewalPaymentCompletedIntegrationEvent
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
            await bus.PublishAsync(new SeatRenewalPaymentFailedIntegrationEvent
            {
                TenantId = evt.TenantId,
                CorrelationId = evt.CorrelationId,
                SeatSubscriptionId = evt.SeatSubscriptionId,
                Reason = payment.FailureReason!
            });
        }
    }
}
