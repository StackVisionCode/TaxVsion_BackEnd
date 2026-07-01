using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Domain.SaaSPayments;
using TaxVision.Payment.Domain.StripeCustomers;
using Wolverine;

namespace TaxVision.Payment.Application.SaaSPayments.IntegrationEvents;

public static class EnrollmentPaymentRequestedHandler
{
    public static async Task Handle(
        EnrollmentPaymentRequestedIntegrationEvent evt,
        ISaaSPaymentRepository payments,
        IStripeCustomerRepository stripeCustomers,
        IStripeGateway stripe,
        IUnitOfWork uow,
        IMessageBus bus,
        CancellationToken ct)
    {
        // 1. Get or create Stripe customer for the tenant
        var existing = await stripeCustomers.GetByTenantIdAsync(evt.TenantId, ct);
        string stripeCustomerId;
        if (existing is null)
        {
            stripeCustomerId = await stripe.GetOrCreateCustomerAsync(evt.TenantId, evt.AdminEmail, ct);
            var newCustomer = StripeCustomer.Create(evt.TenantId, stripeCustomerId, evt.AdminEmail);
            await stripeCustomers.AddAsync(newCustomer, ct);
        }
        else
        {
            stripeCustomerId = existing.StripeCustomerId;
        }

        // 2. Create SaaSPayment record (Pending)
        var payment = SaaSPayment.Create(
            evt.TenantId,
            SaaSPaymentType.Enrollment,
            evt.AmountCents,
            evt.Currency,
            evt.EnrollmentId);
        await payments.AddAsync(payment, ct);
        await uow.SaveChangesAsync(ct);

        // 3. Create payment intent with Stripe
        var description = $"Enrollment payment for tenant {evt.TenantId} - enrollment {evt.EnrollmentId}";
        var intentResult = await stripe.CreatePaymentIntentAsync(stripeCustomerId, evt.AmountCents, evt.Currency, description, ct);
        if (!intentResult.IsSuccess || intentResult.PaymentIntentId is null)
        {
            payment.MarkFailed(intentResult.FailureReason ?? "Failed to create payment intent.");
            await uow.SaveChangesAsync(ct);
            await bus.PublishAsync(new EnrollmentPaymentFailedIntegrationEvent
            {
                TenantId = evt.TenantId,
                CorrelationId = evt.CorrelationId,
                EnrollmentId = evt.EnrollmentId,
                Reason = payment.FailureReason!
            });
            return;
        }

        // 4. Mark as processing
        payment.MarkProcessing(intentResult.PaymentIntentId);
        await uow.SaveChangesAsync(ct);

        // 5. Confirm the payment intent
        var confirmResult = await stripe.ConfirmPaymentIntentAsync(intentResult.PaymentIntentId, ct);

        // 6. Finalize
        if (confirmResult.IsSuccess)
        {
            payment.MarkCompleted();
            await uow.SaveChangesAsync(ct);
            await bus.PublishAsync(new EnrollmentPaymentCompletedIntegrationEvent
            {
                TenantId = evt.TenantId,
                CorrelationId = evt.CorrelationId,
                EnrollmentId = evt.EnrollmentId,
                StripePaymentIntentId = intentResult.PaymentIntentId
            });
        }
        else
        {
            payment.MarkFailed(confirmResult.FailureReason ?? "Payment confirmation failed.");
            await uow.SaveChangesAsync(ct);
            await bus.PublishAsync(new EnrollmentPaymentFailedIntegrationEvent
            {
                TenantId = evt.TenantId,
                CorrelationId = evt.CorrelationId,
                EnrollmentId = evt.EnrollmentId,
                Reason = payment.FailureReason!
            });
        }
    }
}
